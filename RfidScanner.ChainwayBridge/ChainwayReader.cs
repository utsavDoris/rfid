using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BLEDeviceAPI;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace RfidScanner.ChainwayBridge;

public sealed class ChainwayReader : IDisposable
{
    private readonly RFIDWithUHFBEL _reader = RFIDWithUHFBEL.GetInstance();
    private readonly object _scanLock = new();
    private readonly List<ScannedDevice> _scanResults = new();

    private ManualResetEventSlim? _scanDone;
    private ManualResetEventSlim? _connectDone;
    private volatile bool _inventoryRunning;
    private Thread? _readThread;
    private bool _disposed;

    public event Action<ScannedDevice>? DeviceDiscovered;
    public event Action<bool>? ConnectionChanged;
    public event Action<ScannedTag>? TagReceived;
    public event Action<string>? StatusChanged;

    public bool IsConnected { get; private set; }
    public bool IsInventoryRunning { get; private set; }

    public IReadOnlyList<ScannedDevice> Scan(int timeoutSeconds = 20)
    {
        lock (_scanLock)
        {
            _scanResults.Clear();
            _scanDone = new ManualResetEventSlim(false);

            StatusChanged?.Invoke("Scanning BLE devices...");
            _reader.StartBleDeviceWatcher(OnScanDevice);

            if (!_scanDone.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                _reader.StopBleDeviceWatcher();
                throw new TimeoutException("BLE scan timed out.");
            }

            return _scanResults
                .OrderByDescending(d => d.IsChainway)
                .ThenBy(d => d.Name)
                .ToList();
        }
    }

    private void OnScanDevice(DeviceInformation? device, DeviceWatcherStatus status, string? removeId)
    {
        if (device != null)
        {
            var name = string.IsNullOrWhiteSpace(device.Name) ? "Unknown" : device.Name;
            var mac = device.Id.Length >= 17 ? device.Id.Substring(device.Id.Length - 17) : device.Id;
            var scanned = new ScannedDevice
            {
                Name = name,
                DeviceId = device.Id,
                Mac = mac,
                IsChainway = IsChainwayName(name)
            };

            lock (_scanLock)
            {
                if (_scanResults.All(d => d.DeviceId != scanned.DeviceId))
                {
                    _scanResults.Add(scanned);
                    DeviceDiscovered?.Invoke(scanned);
                }
            }
        }

        if (status == DeviceWatcherStatus.EnumerationCompleted || status == DeviceWatcherStatus.Stopped)
        {
            _reader.StopBleDeviceWatcher();
            _scanDone?.Set();
        }
    }

    public void Connect(string deviceId, int timeoutSeconds = 30)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Device ID is required.", nameof(deviceId));

        _connectDone = new ManualResetEventSlim(false);
        _reader.Connect(deviceId, OnConnectionStatusChanged);

        if (!_connectDone.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
            throw new TimeoutException("Connection timed out.");

        if (!IsConnected)
            throw new InvalidOperationException("Failed to connect to reader.");
    }

    private void OnConnectionStatusChanged(BluetoothConnectionStatus status, BluetoothLEDevice? device)
    {
        if (status == BluetoothConnectionStatus.Connected)
        {
            IsConnected = true;
            ConnectionChanged?.Invoke(true);
            _connectDone?.Set();
        }
        else if (status == BluetoothConnectionStatus.Disconnected)
        {
            var wasConnected = IsConnected;
            IsConnected = false;
            IsInventoryRunning = false;
            _inventoryRunning = false;

            if (wasConnected)
                ConnectionChanged?.Invoke(false);

            _connectDone?.Set();
        }
    }

    public void StartInventory()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected.");

        StopInventoryInternal();

        if (!_reader.startInventoryTag())
            throw new InvalidOperationException("startInventoryTag failed.");

        _inventoryRunning = true;
        IsInventoryRunning = true;
        _readThread = new Thread(ReadTagLoop) { IsBackground = true, Name = "ChainwayTagReader" };
        _readThread.Start();
    }

    private void ReadTagLoop()
    {
        while (_inventoryRunning)
        {
            var tags = _reader.ReadTagFromBuffer();
            if (tags != null && tags.Count > 0)
            {
                foreach (var tag in tags)
                {
                    if (string.IsNullOrWhiteSpace(tag.Epc))
                        continue;

                    var rssi = int.TryParse(tag.Rssi, out var parsed) ? parsed : 0;
                    TagReceived?.Invoke(new ScannedTag
                    {
                        Epc = tag.Epc,
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

    public void StopInventory()
    {
        StopInventoryInternal();
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
            catch { /* ignore */ }
        }

        IsInventoryRunning = false;
    }

    public void Disconnect()
    {
        StopInventoryInternal();
        try { _reader.DisConnect(); } catch { /* ignore */ }
        IsConnected = false;
        ConnectionChanged?.Invoke(false);
    }

    public static bool IsChainwayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var deviceName = name;
        return deviceName.IndexOf("Nordic_UART", StringComparison.OrdinalIgnoreCase) >= 0
            || deviceName.IndexOf("Chainway", StringComparison.OrdinalIgnoreCase) >= 0
            || deviceName.Equals("R6", StringComparison.OrdinalIgnoreCase)
            || deviceName.StartsWith("R6 ", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
        _scanDone?.Dispose();
        _connectDone?.Dispose();
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
