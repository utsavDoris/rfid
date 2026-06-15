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
    private readonly object _scanLock = new();
    private readonly List<ScannedDevice> _scanResults = new();

    private TaskCompletionSource<bool>? _connectTcs;
    private volatile bool _inventoryRunning;
    private volatile bool _scanning;
    private bool _epcTidModeConfigured;
    private string? _connectedDeviceId;
    private Thread? _readThread;
    private bool _disposed;

    public event Action<ScannedDevice>? DeviceDiscovered;
    public event Action<string>? DeviceRemoved;
    public event Action? ScanCompleted;
    public event Action<bool>? ConnectionChanged;
    public event Action<ScannedTag>? TagReceived;
    public event Action<string>? StatusChanged;
    public event Action? HardwareTriggerPressed;

    private readonly object _pumpLock = new();
    private Thread? _pumpThread;
    private Form? _triggerForm;
    private ManualResetEventSlim? _pumpReady;
    private volatile bool _pumpRunning;
    private DateTime _lastTriggerUtc = DateTime.MinValue;
    private const int TriggerDebounceMs = 400;

    public bool IsConnected => _isConnected;
    public bool IsInventoryRunning => _isInventoryRunning;
    public bool IsScanning => _scanning;

    private volatile bool _isConnected;
    private volatile bool _isInventoryRunning;

    public ChainwayReader(Action<Action>? invokeOnUi = null)
    {
        _invoke = invokeOnUi ?? (action => action());
    }

    /// <summary>MainForm.btnSearch_Click — start BLE watcher on UI thread.</summary>
    public void BeginScan()
    {
        _invoke(() =>
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
                StatusChanged?.Invoke("Scanning BLE devices...");
                Reader.StartBleDeviceWatcher(OnScanDevice);
            }
        });
    }

    /// <summary>MainForm.btnSearch_Click (stop) — stop BLE watcher.</summary>
    public void StopScan()
    {
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
        StatusChanged?.Invoke(message);
        ScanCompleted?.Invoke();
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

            lock (_scanLock)
            {
                if (_scanResults.All(d => d.Mac != scanned.Mac))
                {
                    _scanResults.Add(scanned);
                    DeviceDiscovered?.Invoke(scanned);
                }
            }
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

        var tcs = new TaskCompletionSource<bool>();

        _invoke(() =>
        {
            try
            {
                EnsureScanStopped();

                _connectTcs = tcs;
                _connectedDeviceId = deviceId;
                _epcTidModeConfigured = false;
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
        Thread.Sleep(350);
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
            EnableHardwareTrigger();
            ConnectionChanged?.Invoke(true);
            StatusChanged?.Invoke("Reader connected.");
            _connectTcs?.TrySetResult(true);
            _connectTcs = null;
        }
        else if (status == BluetoothConnectionStatus.Disconnected)
        {
            var wasConnected = _isConnected;
            StopInventoryInternal();
            DisableHardwareTrigger();

            _isConnected = false;
            _epcTidModeConfigured = false;

            if (wasConnected)
            {
                ConnectionChanged?.Invoke(false);
                StatusChanged?.Invoke("Reader disconnected unexpectedly. Reconnect if needed.");
            }

            if (_connectTcs != null)
            {
                _connectTcs.TrySetException(new InvalidOperationException("Failed to connect to reader."));
                _connectTcs = null;
            }
        }
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
            StopInventoryInternal();
            DisableHardwareTrigger();
            try { Reader.DisConnect(); } catch { /* ignore */ }
            _isConnected = false;
            _epcTidModeConfigured = false;
            _connectedDeviceId = null;
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

        EnsureTriggerPump();
        RunOnPumpThread(() =>
        {
            if (_disposed || _triggerForm == null || _triggerForm.IsDisposed)
                return;

            try
            {
                _triggerForm.Select();
                Reader.SetKeyDownCallBack(OnHardwareKeyDown, _triggerForm);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnableHardwareTrigger failed: {ex.Message}");
            }
        });
    }

    public void DisableHardwareTrigger()
    {
        try { Reader.SetKeyDownCallBack(null, null); } catch { /* ignore */ }

        var form = _triggerForm;
        if (form == null || form.IsDisposed || !_pumpRunning)
            return;

        try
        {
            form.Invoke((Action)(() =>
            {
                try { Reader.SetKeyDownCallBack(null, null); } catch { /* ignore */ }
            }));
        }
        catch
        {
            // Pump is shutting down.
        }
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

    private void EnsureTriggerPump()
    {
        lock (_pumpLock)
        {
            if (_pumpRunning && _triggerForm != null && !_triggerForm.IsDisposed)
                return;

            _pumpReady = new ManualResetEventSlim(false);
            _pumpThread = new Thread(PumpThreadProc)
            {
                IsBackground = true,
                Name = "ChainwayTriggerPump"
            };
            _pumpThread.SetApartmentState(ApartmentState.STA);
            _pumpThread.Start();

            if (!_pumpReady.Wait(TimeSpan.FromSeconds(5)))
                throw new InvalidOperationException("WinForms trigger message pump failed to start.");
        }
    }

    private void PumpThreadProc()
    {
        try
        {
            Application.EnableVisualStyles();
            var form = new Form
            {
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-2000, -2000),
                Size = new Size(1, 1),
                Opacity = 0
            };

            form.Load += (_, _) =>
            {
                _pumpRunning = true;
                _pumpReady?.Set();
            };

            _triggerForm = form;
            Application.Run(form);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Trigger pump thread failed: {ex.Message}");
            _pumpReady?.Set();
        }
        finally
        {
            _pumpRunning = false;
            _triggerForm = null;
        }
    }

    private void RunOnPumpThread(Action action)
    {
        if (_disposed)
            return;

        if (!_pumpRunning || _triggerForm == null || _triggerForm.IsDisposed)
            return;

        var form = _triggerForm;
        try
        {
            if (form.InvokeRequired)
                form.Invoke(action);
            else
                action();
        }
        catch (ObjectDisposedException)
        {
            // Pump is shutting down.
        }
    }

    private void StopTriggerPump()
    {
        var form = _triggerForm;
        if (form == null || form.IsDisposed)
            return;

        try
        {
            if (form.InvokeRequired)
                form.Invoke((Action)(() => Application.ExitThread()));
            else
                Application.ExitThread();
        }
        catch
        {
            // ignore
        }

        try
        {
            _pumpThread?.Join(2000);
        }
        catch
        {
            // ignore
        }

        _pumpThread = null;
        _pumpReady?.Dispose();
        _pumpReady = null;
        _pumpRunning = false;
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

        StopTriggerPump();
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
