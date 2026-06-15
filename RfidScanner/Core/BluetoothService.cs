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
    private readonly WinFormsMessageHost _bleHost = new();
    private readonly ChainwayReader _reader;
    private bool _disposed;

    public event Action<BluetoothDeviceInfo>? DeviceDiscovered;
    public event Action<string>? DeviceRemoved;
    public event Action? ScanCompleted;
    public event Action<RfidTag>? TagReceived;
    public event Action<string>? StatusChanged;
    public event Action<bool>? ConnectionChanged;
    public event Action? HardwareTriggerPressed;

    public bool IsConnected => _reader.IsConnected;
    public bool IsInventoryRunning => _reader.IsInventoryRunning;
    public bool IsScanning => _reader.IsScanning;
    public bool IsChainwayDevice => true;

    public BluetoothService()
    {
        _bleHost.Start();
        _reader = new ChainwayReader(_bleHost);
        _reader.StatusChanged += msg => RunOnUiThread(() => StatusChanged?.Invoke(msg));
        _reader.DeviceDiscovered += device => RunOnUiThread(() => OnDeviceDiscovered(device));
        _reader.DeviceRemoved += id => RunOnUiThread(() => DeviceRemoved?.Invoke(id));
        _reader.ScanCompleted += () => RunOnUiThread(() => ScanCompleted?.Invoke());
        _reader.ConnectionChanged += connected => RunOnUiThread(() => ConnectionChanged?.Invoke(connected));
        _reader.HardwareTriggerPressed += () => RunOnUiThread(() => HardwareTriggerPressed?.Invoke());
        _reader.TagReceived += tag => TagReceived?.Invoke(
            RfidTagMapper.FromScanned(tag.Epc, tag.Tid, tag.User, tag.RssiRaw));
    }

    private static void RunOnUiThread(Action action, bool async = false)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        if (async)
            dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
        else
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
        return Task.Run(() => _reader.StopScan());
    }

    public async Task ConnectAsync(BluetoothDeviceInfo device)
    {
        if (IsScanning)
        {
            await StopScanAsync().ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
        }

        StatusChanged?.Invoke($"Connecting to {device.DeviceLabel} (direct BLE)...");
        await Task.Run(async () =>
        {
            await _reader.ConnectAsync(device.Address).ConfigureAwait(false);
        }).ConfigureAwait(false);

        if (_reader.IsConnected)
            StatusChanged?.Invoke($"Connected to {device.DeviceLabel}.");
    }

    public Task StartInventoryAsync()
    {
        _reader.StartInventory();
        StatusChanged?.Invoke("R6 inventory started — present tags near the reader.");
        return Task.CompletedTask;
    }

    public Task StopInventoryAsync()
    {
        _reader.StopInventory();
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        return Task.Run(() => _reader.Disconnect());
    }

    public int GetPower() => _reader.GetPower();

    public bool SetPower(int power) => _reader.SetPower(power);

    public bool SetBeep(bool enabled) => _reader.SetBeep(enabled);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reader.Dispose();
        _bleHost.Dispose();
    }

    public void ShutdownReader()
    {
        if (_disposed)
            return;

        if (_reader.IsInventoryRunning)
            _reader.StopInventory();
        if (_reader.IsConnected)
            _reader.Disconnect();
    }
}
