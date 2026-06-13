using System.IO;
using System.Net.Sockets;
using InTheHand.Bluetooth;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using DeviceModel = RfidScanner.Models.BluetoothDeviceInfo;
using BleDevice = InTheHand.Bluetooth.BluetoothDevice;

namespace RfidScanner.Core;

public class BluetoothService : IDisposable
{
    private BleDevice? _bleDevice;
    private GattCharacteristic? _notifyCharacteristic;
    private BluetoothClient? _sppClient;
    private Stream? _sppStream;
    private CancellationTokenSource? _readCts;
    private bool _disposed;

    public event Action<byte[]>? DataReceived;
    public event Action<string>? StatusChanged;
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected { get; private set; }

    public async Task<IReadOnlyList<DeviceModel>> ScanDevicesAsync(CancellationToken ct = default)
    {
        var results = new List<DeviceModel>();

        try
        {
            StatusChanged?.Invoke("Scanning BLE devices...");
            var bleDevices = await Bluetooth.ScanForDevicesAsync();
            foreach (var device in bleDevices)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(new DeviceModel
                {
                    Name = device.Name ?? "Unknown",
                    Address = device.Id,
                    IsBle = true,
                    SignalStrength = 0
                });
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"BLE scan error: {ex.Message}");
        }

        try
        {
            StatusChanged?.Invoke("Scanning paired SPP devices...");
            using var client = new BluetoothClient();
            foreach (var device in client.PairedDevices)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(new DeviceModel
                {
                    Name = device.DeviceName,
                    Address = device.DeviceAddress.ToString(),
                    IsBle = false,
                    SignalStrength = 0
                });
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"SPP scan error: {ex.Message}");
        }

        StatusChanged?.Invoke($"Found {results.Count} device(s).");
        return results;
    }

    public async Task ConnectAsync(DeviceModel device, CancellationToken ct = default)
    {
        await DisconnectAsync();

        if (device.IsBle)
            await ConnectBleAsync(device, ct);
        else
            await ConnectSppAsync(device, ct);
    }

    private async Task ConnectBleAsync(DeviceModel device, CancellationToken ct)
    {
        StatusChanged?.Invoke($"Connecting BLE: {device.DisplayName}...");

        var bleDevices = await Bluetooth.ScanForDevicesAsync();
        _bleDevice = bleDevices.FirstOrDefault(d => d.Id == device.Address)
            ?? throw new InvalidOperationException("BLE device not found. Run Scan Devices again.");

        await _bleDevice.Gatt.ConnectAsync();

        var services = await _bleDevice.Gatt.GetPrimaryServicesAsync();
        GattCharacteristic? notifyChar = null;

        foreach (var service in services)
        {
            ct.ThrowIfCancellationRequested();
            var characteristics = await service.GetCharacteristicsAsync();
            notifyChar = characteristics.FirstOrDefault(c =>
                c.Properties.HasFlag(GattCharacteristicProperties.Notify) ||
                c.Properties.HasFlag(GattCharacteristicProperties.Indicate));

            if (notifyChar != null)
                break;
        }

        if (notifyChar == null)
            throw new InvalidOperationException("No Notify/Indicate characteristic found.");

        _notifyCharacteristic = notifyChar;
        _notifyCharacteristic.CharacteristicValueChanged += OnBleDataReceived;
        await _notifyCharacteristic.StartNotificationsAsync();

        IsConnected = true;
        ConnectionChanged?.Invoke(true);
        StatusChanged?.Invoke($"Connected (BLE): {device.DisplayName}");
    }

    private async Task ConnectSppAsync(DeviceModel device, CancellationToken ct)
    {
        StatusChanged?.Invoke($"Connecting SPP: {device.DisplayName}...");

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            _sppClient = new BluetoothClient();
            var address = BluetoothAddress.Parse(device.Address);
            var endpoint = new BluetoothEndPoint(address, InTheHand.Net.Bluetooth.BluetoothService.SerialPort);
            _sppClient.Connect(endpoint);
            _sppStream = _sppClient.GetStream();
        }, ct);

        _readCts = new CancellationTokenSource();
        _ = Task.Run(() => ReadSppLoop(_readCts.Token), _readCts.Token);

        IsConnected = true;
        ConnectionChanged?.Invoke(true);
        StatusChanged?.Invoke($"Connected (SPP): {device.DisplayName}");
    }

    private void OnBleDataReceived(object? sender, GattCharacteristicValueChangedEventArgs e)
    {
        if (e.Value != null && e.Value.Length > 0)
            DataReceived?.Invoke(e.Value);
    }

    private void ReadSppLoop(CancellationToken ct)
    {
        var buffer = new byte[4096];

        try
        {
            while (!ct.IsCancellationRequested && _sppStream != null && _sppClient?.Connected == true)
            {
                if (_sppStream is NetworkStream netStream && !netStream.DataAvailable)
                {
                    Thread.Sleep(50);
                    continue;
                }

                var read = _sppStream.Read(buffer, 0, buffer.Length);
                if (read > 0)
                {
                    var chunk = new byte[read];
                    Array.Copy(buffer, chunk, read);
                    DataReceived?.Invoke(chunk);
                }
                else
                {
                    Thread.Sleep(50);
                }
            }
        }
        catch (IOException)
        {
            if (!ct.IsCancellationRequested)
                StatusChanged?.Invoke("SPP connection closed.");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"SPP read error: {ex.Message}");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                _ = DisconnectAsync();
        }
    }

    public async Task DisconnectAsync()
    {
        _readCts?.Cancel();
        _readCts?.Dispose();
        _readCts = null;

        if (_notifyCharacteristic != null)
        {
            _notifyCharacteristic.CharacteristicValueChanged -= OnBleDataReceived;
            try { await _notifyCharacteristic.StopNotificationsAsync(); } catch { /* ignore */ }
            _notifyCharacteristic = null;
        }

        if (_bleDevice?.Gatt.IsConnected == true)
        {
            try { _bleDevice.Gatt.Disconnect(); } catch { /* ignore */ }
        }
        _bleDevice = null;

        try { _sppStream?.Close(); } catch { /* ignore */ }
        _sppStream = null;

        try { _sppClient?.Close(); } catch { /* ignore */ }
        _sppClient?.Dispose();
        _sppClient = null;

        if (IsConnected)
        {
            IsConnected = false;
            ConnectionChanged?.Invoke(false);
            StatusChanged?.Invoke("Disconnected.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = DisconnectAsync();
    }
}
