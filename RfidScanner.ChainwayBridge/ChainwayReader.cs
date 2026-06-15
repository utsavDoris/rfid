using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BLEDeviceAPI;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace RfidScanner.ChainwayBridge;

/// <summary>
/// Mirrors win_ble_V1.2 sound code MainForm.cs + InventoryForm.cs BLE flows.
/// WinRT DeviceWatcher must run on a UI thread with a message pump (official WinForms UI thread).
/// </summary>
public sealed class ChainwayReader : IDisposable
{
    private RFIDWithUHFBEL? _readerInstance;
    private RFIDWithUHFBEL Reader => _readerInstance ?? (_readerInstance = RFIDWithUHFBEL.GetInstance());
    private readonly Action<Action> _invoke;
    private readonly Action<Action> _post;
    private readonly WinFormsMessageHost _messageHost;
    private readonly bool _ownMessageHost;
    private readonly object _scanLock = new();
    private readonly List<ScannedDevice> _scanResults = new();

    private TaskCompletionSource<bool>? _connectTcs;
    private volatile bool _inventoryRunning;
    private volatile bool _scanning;
    private bool _epcTidModeConfigured;
    private string? _connectedDeviceId;
    private Thread? _readThread;
    private bool _disposed;

    private volatile bool _intentionalDisconnect;
    private volatile bool _reconnectInProgress;
    private int _reconnectAttempts;
    private DateTime _connectedAtUtc = DateTime.MinValue;
    private System.Threading.Timer? _keepAliveTimer;
    private System.Threading.Timer? _reconnectTimer;

    private const int ConnectStabilizationMs = 4000;
    private const int MaxSilentReconnectAttempts = 4;
    private const int KeepAliveIntervalMs = 20000;

    public event Action<ScannedDevice>? DeviceDiscovered;
    public event Action<string>? DeviceRemoved;
    public event Action? ScanCompleted;
    public event Action<bool>? ConnectionChanged;
    public event Action<ScannedTag>? TagReceived;
    public event Action<string>? StatusChanged;
    public event Action? HardwareTriggerPressed;

    private DateTime _lastTriggerUtc = DateTime.MinValue;
    private const int TriggerDebounceMs = 400;

    public bool IsConnected => _isConnected;
    public bool IsInventoryRunning => _isInventoryRunning;
    public bool IsScanning => _scanning;

    private volatile bool _isConnected;
    private volatile bool _isInventoryRunning;

    /// <summary>Uses WinForms STA message pump (Chainway MainForm) — not WPF dispatcher.</summary>
    public ChainwayReader(WinFormsMessageHost? messageHost = null)
    {
        _ownMessageHost = messageHost == null;
        _messageHost = messageHost ?? new WinFormsMessageHost();
        _messageHost.Start();
        _invoke = _messageHost.Invoke;
        _post = _messageHost.Post;
    }

    /// <summary>MainForm.btnSearch_Click — start BLE watcher on UI thread.</summary>
    public void BeginScan()
    {
        // Post (non-blocking) so the WPF dispatcher can render devices while scan runs.
        _post(() =>
        {
            lock (_scanLock)
            {
                if (_scanning)
                    return;

                if (IsConnected)
                {
                    StatusChanged?.Invoke("Disconnect the reader before scanning for devices.");
                    return;
                }

                _scanResults.Clear();
                _scanning = true;
            }

            // Must not hold _scanLock here — watcher can call OnScanDevice synchronously and deadlock.
            StatusChanged?.Invoke("Scanning BLE devices...");
            Reader.StartBleDeviceWatcher(OnScanDevice);
        });
    }

    /// <summary>MainForm.btnSearch_Click (stop) — stop BLE watcher.</summary>
    public void StopScan()
    {
        // Synchronous on BLE thread so connect can wait for watcher to fully stop.
        _invoke(() =>
        {
            lock (_scanLock)
            {
                if (!_scanning)
                    return;

                Reader.StopBleDeviceWatcher();
                FinishScan($"Scan stopped. Found {_scanResults.Count} device(s).");
            }
        });
    }

    private void FinishScan(string message)
    {
        _scanning = false;

        List<ScannedDevice> snapshot;
        lock (_scanLock)
            snapshot = _scanResults.ToList();

        // Re-publish all results so the UI list is always filled when scan ends.
        foreach (var device in snapshot)
            PublishDeviceDiscovered(device);

        StatusChanged?.Invoke(message);
        ScanCompleted?.Invoke();
    }

    private void PublishDeviceDiscovered(ScannedDevice device)
    {
        DeviceDiscovered?.Invoke(device);
    }

    /// <summary>MainForm.ScanDeviceEventHandler</summary>
    private void OnScanDevice(DeviceInformation? device, DeviceWatcherStatus status, string? removeId)
    {
        if (device != null)
        {
            var name = string.IsNullOrWhiteSpace(device.Name) ? "Unknown" : device.Name;
            var mac = device.Id.Length >= 17
                ? device.Id.Substring(device.Id.Length - 17, 17)
                : device.Id;

            var scanned = new ScannedDevice
            {
                Name = name,
                DeviceId = device.Id,
                Mac = mac,
                IsChainway = IsChainwayName(name)
            };

            ScannedDevice? discovered = null;
            lock (_scanLock)
            {
                if (_scanResults.All(d => d.DeviceId != scanned.DeviceId && d.Mac != scanned.Mac))
                {
                    _scanResults.Add(scanned);
                    discovered = scanned;
                }
            }

            if (discovered != null)
                PublishDeviceDiscovered(discovered);
        }
        else if (!string.IsNullOrEmpty(removeId))
        {
            // Windows BLE watcher fires "removed" while connected — ignore to avoid popup/disconnect churn.
            if (_isConnected || !_scanning)
                return;

            if (!string.IsNullOrEmpty(_connectedDeviceId) &&
                string.Equals(removeId, _connectedDeviceId, StringComparison.OrdinalIgnoreCase))
                return;

            lock (_scanLock)
            {
                var removed = _scanResults.FirstOrDefault(d => d.DeviceId == removeId);
                if (removed != null)
                {
                    _scanResults.Remove(removed);
                    DeviceRemoved?.Invoke(removeId!);
                }
            }
        }

        if (status == DeviceWatcherStatus.Stopped)
        {
            System.Diagnostics.Debug.WriteLine("BLE scan stopped.");
        }
        else if (status == DeviceWatcherStatus.EnumerationCompleted)
        {
            System.Diagnostics.Debug.WriteLine("BLE scan completed.");
            _invoke(() =>
            {
                lock (_scanLock)
                {
                    if (!_scanning)
                        return;

                    Reader.StopBleDeviceWatcher();
                    FinishScan($"Scan complete. Found {_scanResults.Count} device(s).");
                }
            });
        }
    }

    /// <summary>MainForm.btnConn_Click — Connect using BLE device Id from scan.</summary>
    public Task ConnectAsync(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Device ID is required.", nameof(deviceId));

        if (!IsBleScanDeviceId(deviceId))
            throw new InvalidOperationException(
                "Use Scan Devices and select R6 from the list. Do not connect via Windows paired Bluetooth.");

        var tcs = new TaskCompletionSource<bool>();

        _invoke(() =>
        {
            try
            {
                PrepareBleLinkForConnect();

                _intentionalDisconnect = false;
                _reconnectAttempts = 0;
                _reconnectInProgress = false;
                _connectTcs = tcs;
                _connectedDeviceId = deviceId;
                _epcTidModeConfigured = false;
                StatusChanged?.Invoke("Connecting via BLE (no Windows pairing)...");
                Reader.Connect(deviceId, OnConnectionStatusChanged);
            }
            catch (Exception ex)
            {
                _connectTcs = null;
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    /// <summary>Stop watcher and clear stale GATT link before direct BLE connect (avoids pair popup churn).</summary>
    private void PrepareBleLinkForConnect()
    {
        EnsureScanStopped();

        try { Reader.DisConnect(); } catch { /* clear stale session */ }
        Thread.Sleep(500);
    }

    private void EnsureScanStopped()
    {
        try
        {
            Reader.StopBleDeviceWatcher();
        }
        catch
        {
            // ignore
        }

        _scanning = false;
        Thread.Sleep(400);
    }

    private void OnConnectionStatusChanged(BluetoothConnectionStatus status, BluetoothLEDevice? device)
    {
        _invoke(() => HandleConnectionStatusChanged(status, device));
    }

    private void HandleConnectionStatusChanged(BluetoothConnectionStatus status, BluetoothLEDevice? device)
    {
        if (status == BluetoothConnectionStatus.Connected)
        {
            _isConnected = true;
            _connectedAtUtc = DateTime.UtcNow;
            _reconnectAttempts = 0;
            _reconnectInProgress = false;
            CancelReconnectTimer();
            StartKeepAlive();
            EnableHardwareTrigger();
            ConnectionChanged?.Invoke(true);
            StatusChanged?.Invoke("R6 connected (direct BLE — no Windows pairing needed).");
            _connectTcs?.TrySetResult(true);
            _connectTcs = null;
            return;
        }

        if (status != BluetoothConnectionStatus.Disconnected)
            return;

        // Ignore brief disconnect glitches right after connect (Windows BLE stack).
        if (IsWithinConnectStabilization() && !_intentionalDisconnect)
        {
            System.Diagnostics.Debug.WriteLine("Ignoring disconnect during connect stabilization.");
            TrySilentReconnect();
            return;
        }

        if (_intentionalDisconnect)
        {
            ApplyDisconnectedState(wasConnected: _isConnected, notifyUnexpected: false);
            return;
        }

        // Unexpected drop — try silent reconnect before telling the UI.
        if (!string.IsNullOrEmpty(_connectedDeviceId) && _reconnectAttempts < MaxSilentReconnectAttempts)
        {
            TrySilentReconnect();
            return;
        }

        ApplyDisconnectedState(wasConnected: true, notifyUnexpected: true);
    }

    private bool IsWithinConnectStabilization()
        => (DateTime.UtcNow - _connectedAtUtc).TotalMilliseconds < ConnectStabilizationMs;

    private void TrySilentReconnect()
    {
        if (_disposed || _intentionalDisconnect || string.IsNullOrEmpty(_connectedDeviceId))
            return;

        if (_reconnectInProgress)
            return;

        _reconnectInProgress = true;
        _reconnectAttempts++;

        CancelReconnectTimer();
        _reconnectTimer = new System.Threading.Timer(_ =>
        {
            _invoke(() =>
            {
                if (_disposed || _intentionalDisconnect || _isConnected)
                {
                    _reconnectInProgress = false;
                    return;
                }

                try
                {
                    StatusChanged?.Invoke($"Reconnecting to R6 (attempt {_reconnectAttempts}/{MaxSilentReconnectAttempts})...");
                    EnsureScanStopped();
                    try { Reader.DisConnect(); } catch { /* ignore */ }
                    Thread.Sleep(300);

                    var tcs = new TaskCompletionSource<bool>();
                    _connectTcs = tcs;
                    Reader.Connect(_connectedDeviceId!, OnConnectionStatusChanged);

                    // If reconnect does not complete quickly, allow another attempt.
                    _reconnectTimer?.Dispose();
                    _reconnectTimer = new System.Threading.Timer(__ =>
                    {
                        _invoke(() =>
                        {
                            if (_isConnected || _intentionalDisconnect)
                            {
                                _reconnectInProgress = false;
                                return;
                            }

                            _reconnectInProgress = false;
                            if (_reconnectAttempts < MaxSilentReconnectAttempts)
                                TrySilentReconnect();
                            else
                                ApplyDisconnectedState(wasConnected: true, notifyUnexpected: true);
                        });
                    }, null, 8000, Timeout.Infinite);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Silent reconnect failed: {ex.Message}");
                    _reconnectInProgress = false;
                    if (_reconnectAttempts >= MaxSilentReconnectAttempts)
                        ApplyDisconnectedState(wasConnected: true, notifyUnexpected: true);
                    else
                        TrySilentReconnect();
                }
            });
        }, null, 400, Timeout.Infinite);
    }

    private void ApplyDisconnectedState(bool wasConnected, bool notifyUnexpected)
    {
        CancelReconnectTimer();
        StopKeepAlive();
        StopInventoryInternal();
        DisableHardwareTrigger();

        _isConnected = false;
        _reconnectInProgress = false;
        _epcTidModeConfigured = false;

        if (wasConnected)
        {
            ConnectionChanged?.Invoke(false);
            StatusChanged?.Invoke(notifyUnexpected
                ? "R6 disconnected — select device and Connect again."
                : "R6 disconnected.");
        }

        if (_connectTcs != null)
        {
            _connectTcs.TrySetException(new InvalidOperationException("Failed to connect to reader."));
            _connectTcs = null;
        }
    }

    private void StartKeepAlive()
    {
        StopKeepAlive();
        _keepAliveTimer = new System.Threading.Timer(_ =>
        {
            if (_disposed || !_isConnected || _intentionalDisconnect)
                return;

            _invoke(() =>
            {
                try
                {
                    if (IsConnected)
                        Reader.GetPower();
                }
                catch
                {
                    // ignore — reconnect logic handles real drops
                }
            });
        }, null, KeepAliveIntervalMs, KeepAliveIntervalMs);
    }

    private void StopKeepAlive()
    {
        _keepAliveTimer?.Dispose();
        _keepAliveTimer = null;
    }

    private void CancelReconnectTimer()
    {
        _reconnectTimer?.Dispose();
        _reconnectTimer = null;
    }

    /// <summary>InventoryForm.btnInventory_Click + readTag background thread.</summary>
    public void StartInventory()
    {
        _invoke(() =>
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected.");

            if (IsInventoryRunning)
                return;

            if (!_epcTidModeConfigured)
            {
                if (!Reader.SetEPCTIDMode())
                    throw new InvalidOperationException("SetEPCTIDMode failed — reader may not support EPC+TID mode.");
                _epcTidModeConfigured = true;
            }

            if (!Reader.startInventoryTag())
                throw new InvalidOperationException("startInventoryTag failed.");

            _inventoryRunning = true;
            _isInventoryRunning = true;
            _readThread = new Thread(ReadTagLoop) { IsBackground = true, Name = "ChainwayTagReader" };
            _readThread.Start();
        });
    }

    /// <summary>InventoryForm_EPCTIDUSER.readTag — EPC+TID buffer read.</summary>
    private void ReadTagLoop()
    {
        while (_inventoryRunning && IsConnected)
        {
            try
            {
                var tags = Reader.ReadTagFromBuffer_EPCTIDUSER();
                if (tags != null && tags.Count > 0)
                {
                    foreach (var tag in tags)
                    {
                        if (!_inventoryRunning || !IsConnected)
                            break;

                        if (string.IsNullOrWhiteSpace(tag.Epc) && string.IsNullOrWhiteSpace(tag.Tid))
                            continue;

                        var rssiRaw = tag.Rssi ?? string.Empty;
                        var rssi = ParseRssi(rssiRaw);
                        TagReceived?.Invoke(new ScannedTag
                        {
                            Epc = tag.Epc ?? string.Empty,
                            RssiRaw = rssiRaw,
                            Rssi = rssi,
                            Tid = tag.Tid ?? string.Empty,
                            User = tag.User ?? string.Empty
                        });
                    }
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
            catch
            {
                if (_inventoryRunning)
                    Thread.Sleep(10);
            }
        }
    }

    /// <summary>InventoryForm.btnStop_Click</summary>
    public void StopInventory()
    {
        _invoke(StopInventoryInternal);
    }

    private void StopInventoryInternal()
    {
        if (!IsInventoryRunning && (_readThread == null || !_readThread.IsAlive))
            return;

        _inventoryRunning = false;

        var thread = _readThread;
        _readThread = null;
        thread?.Join(1000);

        if (IsConnected && IsInventoryRunning)
        {
            try
            {
                Thread.Sleep(100);
                Reader.stopInventoryTag();
            }
            catch
            {
                // ignore
            }
        }

        _isInventoryRunning = false;
    }

    /// <summary>MainForm.btnDisConn_Click</summary>
    public void Disconnect()
    {
        _invoke(() =>
        {
            _intentionalDisconnect = true;
            CancelReconnectTimer();
            _reconnectInProgress = false;
            StopKeepAlive();
            StopInventoryInternal();
            DisableHardwareTrigger();
            try { Reader.DisConnect(); } catch { /* ignore */ }
            _isConnected = false;
            _epcTidModeConfigured = false;
            _connectedDeviceId = null;
            StatusChanged?.Invoke("Disconnected from app.");
            ConnectionChanged?.Invoke(false);
        });
    }

    public int GetPower()
    {
        int power = -1;
        _invoke(() => 
        {
            if (IsConnected) power = Reader.GetPower();
        });
        return power;
    }

    public bool SetPower(int power)
    {
        bool success = false;
        _invoke(() => 
        {
            if (IsConnected) success = Reader.SetPower(power);
        });
        return success;
    }

    /// <summary>InventoryForm SetKeyDownCallBack — R6 physical trigger toggles scan.</summary>
    public void EnableHardwareTrigger()
    {
        if (_disposed)
            return;

        _invoke(() =>
        {
            if (_disposed)
                return;

            var form = _messageHost.Form;
            if (form == null || form.IsDisposed)
                return;

            try
            {
                form.Select();
                Reader.SetKeyDownCallBack(OnHardwareKeyDown, form);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnableHardwareTrigger failed: {ex.Message}");
            }
        });
    }

    public void DisableHardwareTrigger()
    {
        _invoke(() =>
        {
            try { Reader.SetKeyDownCallBack(null, null); } catch { /* ignore */ }
        });
    }

    private void OnHardwareKeyDown(int keyCode)
    {
        if (_disposed)
            return;

        var now = DateTime.UtcNow;
        if ((now - _lastTriggerUtc).TotalMilliseconds < TriggerDebounceMs)
            return;
        _lastTriggerUtc = now;

        try
        {
            HardwareTriggerPressed?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Hardware trigger callback failed: {ex.Message}");
        }
    }

    /// <summary>SettingsForm SetBeep — mute/unmute R6 reader beep on tag read.</summary>
    public bool SetBeep(bool enabled)
    {
        bool success = false;
        _invoke(() =>
        {
            if (IsConnected)
                success = Reader.SetBeep(enabled);
        });
        return success;
    }

    public static bool IsBleScanDeviceId(string deviceId)
        => !string.IsNullOrWhiteSpace(deviceId)
           && deviceId.IndexOf("BluetoothLE#", StringComparison.OrdinalIgnoreCase) >= 0;

    public static bool IsChainwayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name!.IndexOf("Nordic_UART", StringComparison.OrdinalIgnoreCase) >= 0
            || name!.IndexOf("Chainway", StringComparison.OrdinalIgnoreCase) >= 0
            || name!.Equals("R6", StringComparison.OrdinalIgnoreCase)
            || name!.StartsWith("R6 ", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseRssi(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        var cleaned = raw!.Trim()
            .Replace("dBm", string.Empty)
            .Replace("DBM", string.Empty)
            .Trim();

        if (int.TryParse(cleaned, out var value))
            return value;

        foreach (var part in cleaned.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out value))
                return value;
        }

        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_scanning)
            {
                try { Reader.StopBleDeviceWatcher(); } catch { /* ignore */ }
                _scanning = false;
            }
        }
        catch
        {
            // ignore
        }

        DisableHardwareTrigger();
        StopInventoryForShutdown();
        ShutdownConnection();

        CancelReconnectTimer();
        StopKeepAlive();

        if (_ownMessageHost)
            _messageHost.Dispose();
    }

    private void StopInventoryForShutdown()
    {
        if (!IsInventoryRunning && (_readThread == null || !_readThread.IsAlive))
            return;

        _inventoryRunning = false;

        var thread = _readThread;
        _readThread = null;
        try { thread?.Join(1000); } catch { /* ignore */ }

        if (_isConnected)
        {
            try
            {
                Thread.Sleep(100);
                Reader.stopInventoryTag();
            }
            catch
            {
                // ignore
            }
        }

        _isInventoryRunning = false;
    }

    private void ShutdownConnection()
    {
        _intentionalDisconnect = true;
        try { Reader.DisConnect(); } catch { /* ignore */ }
        _isConnected = false;
        _epcTidModeConfigured = false;
        _connectedDeviceId = null;
    }
}

public sealed class ScannedDevice
{
    public string Name { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string Mac { get; set; } = string.Empty;
    public bool IsChainway { get; set; }
    public bool IsSystemPaired { get; set; }
}

public sealed class ScannedTag
{
    public string Epc { get; set; } = string.Empty;
    public string RssiRaw { get; set; } = string.Empty;
    public int Rssi { get; set; }
    public string Tid { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
}
