using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using RfidScanner.ChainwayBridge;
using RfidScanner.Models;

namespace RfidScanner.Core;

/// <summary>
/// WPF service over Chainway win_ble_V1.2 BLEDeviceAPI — scan/connect run on UI thread like MainForm.cs.
/// </summary>
public class BluetoothService : IDisposable
{
    private readonly ChainwayReader _reader;
    private bool _disposed;

    public event Action<BluetoothDeviceInfo>? DeviceDiscovered;
    public event Action<string>? DeviceRemoved;
    public event Action? ScanCompleted;
    public event Action<RfidTag>? TagReceived;
    public event Action<string>? StatusChanged;
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected => _reader.IsConnected;
    public bool IsInventoryRunning => _reader.IsInventoryRunning;
    public bool IsScanning => _reader.IsScanning;
    public bool IsChainwayDevice => true;

    public BluetoothService()
    {
        _reader = new ChainwayReader(RunOnUiThread);
        _reader.StatusChanged += msg => StatusChanged?.Invoke(msg);
        _reader.DeviceDiscovered += OnDeviceDiscovered;
        _reader.DeviceRemoved += id => DeviceRemoved?.Invoke(id);
        _reader.ScanCompleted += () => ScanCompleted?.Invoke();
        _reader.ConnectionChanged += connected => ConnectionChanged?.Invoke(connected);
        _reader.TagReceived += tag => TagReceived?.Invoke(new RfidTag
        {
            TagId = tag.Epc,
            TagType = "EPC",
            Rssi = tag.Rssi
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action, DispatcherPriority.Normal);
    }

    private void OnDeviceDiscovered(ScannedDevice device)
    {
        DeviceDiscovered?.Invoke(new BluetoothDeviceInfo
        {
            Name = device.Name,
            Address = device.DeviceId,
            Mac = device.Mac,
            IsBle = true,
            IsChainway = device.IsChainway
        });
    }

    public Task BeginScanAsync()
    {
        _reader.BeginScan();
        return Task.CompletedTask;
    }

    public Task StopScanAsync()
    {
        _reader.StopScan();
        return Task.CompletedTask;
    }

    public async Task ConnectAsync(BluetoothDeviceInfo device)
    {
        StatusChanged?.Invoke($"Connecting to {device.DeviceLabel}...");
        await _reader.ConnectAsync(device.Address).ConfigureAwait(true);
        StatusChanged?.Invoke($"Connected to {device.DeviceLabel}.");
    }

    public Task StartInventoryAsync()
    {
        RunOnUiThread(() =>
        {
            _reader.StartInventory();
            StatusChanged?.Invoke("R6 inventory started — present tags near the reader.");
        });
        return Task.CompletedTask;
    }

    public Task StopInventoryAsync()
    {
        RunOnUiThread(_reader.StopInventory);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        RunOnUiThread(() =>
        {
            _reader.Disconnect();
            StatusChanged?.Invoke("Disconnected.");
        });
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reader.Dispose();
    }
}
