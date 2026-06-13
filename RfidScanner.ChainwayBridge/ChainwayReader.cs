using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BLEDeviceAPI;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace RfidScanner.ChainwayBridge;

/// <summary>
/// Mirrors win_ble_V1.2 sound code MainForm.cs + InventoryForm.cs BLE flows.
/// WinRT DeviceWatcher must run on a UI thread with a message pump (official WinForms UI thread).
/// </summary>
public sealed class ChainwayReader : IDisposable
{
    private readonly RFIDWithUHFBEL _reader = RFIDWithUHFBEL.GetInstance();
    private readonly Action<Action> _invoke;
    private readonly object _scanLock = new();
    private readonly List<ScannedDevice> _scanResults = new();

    private TaskCompletionSource<bool>? _connectTcs;
    private volatile bool _inventoryRunning;
    private volatile bool _scanning;
    private Thread? _readThread;
    private bool _disposed;

    public event Action<ScannedDevice>? DeviceDiscovered;
    public event Action<string>? DeviceRemoved;
    public event Action? ScanCompleted;
    public event Action<bool>? ConnectionChanged;
    public event Action<ScannedTag>? TagReceived;
    public event Action<string>? StatusChanged;

    public bool IsConnected { get; private set; }
    public bool IsInventoryRunning { get; private set; }
    public bool IsScanning => _scanning;

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

                _scanResults.Clear();
                _scanning = true;
                StatusChanged?.Invoke("Scanning BLE devices...");
                _reader.StartBleDeviceWatcher(OnScanDevice);
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

                _reader.StopBleDeviceWatcher();
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
            lock (_scanLock)
            {
                var removed = _scanResults.FirstOrDefault(d => d.DeviceId == removeId);
                if (removed != null)
                {
                    _scanResults.Remove(removed);
                    DeviceRemoved?.Invoke(removeId);
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

                    _reader.StopBleDeviceWatcher();
                    FinishScan($"Scan complete. Found {_scanResults.Count} device(s).");
                }
            });
        }
    }

    /// <summary>MainForm.btnConn_Click — Connect using full Windows BLE device Id (SubItems[2]).</summary>
    public Task ConnectAsync(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Device ID is required.", nameof(deviceId));

        var tcs = new TaskCompletionSource<bool>();

        _invoke(() =>
        {
            _connectTcs = tcs;
            _reader.Connect(deviceId, OnConnectionStatusChanged);
        });

        return tcs.Task;
    }

    private void OnConnectionStatusChanged(BluetoothConnectionStatus status, BluetoothLEDevice? device)
    {
        if (status == BluetoothConnectionStatus.Connected)
        {
            IsConnected = true;
            ConnectionChanged?.Invoke(true);
            _connectTcs?.TrySetResult(true);
            _connectTcs = null;
        }
        else if (status == BluetoothConnectionStatus.Disconnected)
        {
            var wasConnected = IsConnected;
            IsConnected = false;
            IsInventoryRunning = false;
            _inventoryRunning = false;

            if (wasConnected)
                ConnectionChanged?.Invoke(false);

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

            StopInventoryInternal();

            if (!_reader.SetEPCTIDMode())
                throw new InvalidOperationException("SetEPCTIDMode failed — reader may not support EPC+TID mode.");

            if (!_reader.startInventoryTag())
                throw new InvalidOperationException("startInventoryTag failed.");

            _inventoryRunning = true;
            IsInventoryRunning = true;
            _readThread = new Thread(ReadTagLoop) { IsBackground = true, Name = "ChainwayTagReader" };
            _readThread.Start();
        });
    }

    /// <summary>InventoryForm_EPCTIDUSER.readTag — EPC+TID buffer read.</summary>
    private void ReadTagLoop()
    {
        while (_inventoryRunning)
        {
            var tags = _reader.ReadTagFromBuffer_EPCTIDUSER();
            if (tags != null && tags.Count > 0)
            {
                foreach (var tag in tags)
                {
                    if (string.IsNullOrWhiteSpace(tag.Epc) && string.IsNullOrWhiteSpace(tag.Tid))
                        continue;

                    var rssi = int.TryParse(tag.Rssi, out var parsed) ? parsed : 0;
                    TagReceived?.Invoke(new ScannedTag
                    {
                        Epc = tag.Epc ?? string.Empty,
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
    }

    /// <summary>InventoryForm.btnStop_Click</summary>
    public void StopInventory()
    {
        _invoke(StopInventoryInternal);
    }

    private void StopInventoryInternal()
    {
        _inventoryRunning = false;
        _readThread?.Join(500);
        _readThread = null;

        if (IsConnected)
        {
            try
            {
                Thread.Sleep(100);
                _reader.stopInventoryTag();
            }
            catch
            {
                // ignore
            }
        }

        IsInventoryRunning = false;
    }

    /// <summary>MainForm.btnDisConn_Click</summary>
    public void Disconnect()
    {
        _invoke(() =>
        {
            StopInventoryInternal();
            try { _reader.DisConnect(); } catch { /* ignore */ }
            IsConnected = false;
            ConnectionChanged?.Invoke(false);
        });
    }

    public static bool IsChainwayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.IndexOf("Nordic_UART", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Chainway", StringComparison.OrdinalIgnoreCase) >= 0
            || name.Equals("R6", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("R6 ", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_scanning)
                StopScan();
        }
        catch
        {
            // ignore
        }

        Disconnect();
    }
}

public sealed class ScannedDevice
{
    public string Name { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string Mac { get; set; } = string.Empty;
    public bool IsChainway { get; set; }
}

public sealed class ScannedTag
{
    public string Epc { get; set; } = string.Empty;
    public int Rssi { get; set; }
    public string Tid { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
}
