using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RfidScanner.ChainwayBridge;
using RfidScanner.Models;

namespace RfidScanner.Core;

/// <summary>
/// WPF-facing Bluetooth service backed by Chainway's official BLEDeviceAPI (win_ble_V1.2).
/// </summary>
public class BluetoothService : IDisposable
{
    private readonly ChainwayReader _reader = new();
    private bool _disposed;

    public event Action<byte[]>? DataReceived;
    public event Action<RfidTag>? TagReceived;
    public event Action<string>? StatusChanged;
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected => _reader.IsConnected;
    public bool IsInventoryRunning => _reader.IsInventoryRunning;
    public bool IsChainwayDevice => true;

    public BluetoothService()
    {
        _reader.StatusChanged += msg => StatusChanged?.Invoke(msg);
        _reader.ConnectionChanged += connected => ConnectionChanged?.Invoke(connected);
        _reader.TagReceived += tag => TagReceived?.Invoke(new RfidTag
        {
            TagId = tag.Epc,
            TagType = "EPC",
            Rssi = tag.Rssi
        });
    }

    public Task<IReadOnlyList<BluetoothDeviceInfo>> ScanDevicesAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var devices = _reader.Scan(20);
            ct.ThrowIfCancellationRequested();

            return (IReadOnlyList<BluetoothDeviceInfo>)devices.Select(d => new BluetoothDeviceInfo
            {
                Name = d.Name,
                Address = d.DeviceId,
                Mac = d.Mac,
                IsBle = true,
                IsChainway = d.IsChainway
            }).ToList();
        }, ct);
    }

    public Task ConnectAsync(BluetoothDeviceInfo device, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            StatusChanged?.Invoke($"Connecting to {device.DeviceLabel}...");
            _reader.Connect(device.Address, 30);
            ct.ThrowIfCancellationRequested();
            StatusChanged?.Invoke($"Connected to {device.DeviceLabel}.");
        }, ct);
    }

    public Task StartInventoryAsync()
    {
        return Task.Run(() =>
        {
            _reader.StartInventory();
            StatusChanged?.Invoke("R6 inventory started — present tags near the reader.");
        });
    }

    public Task StopInventoryAsync()
    {
        return Task.Run(_reader.StopInventory);
    }

    public Task DisconnectAsync()
    {
        return Task.Run(() =>
        {
            _reader.Disconnect();
            StatusChanged?.Invoke("Disconnected.");
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reader.Dispose();
    }
}
