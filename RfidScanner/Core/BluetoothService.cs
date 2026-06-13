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
    private GattCharacteristic? _writeCharacteristic;
    private BluetoothClient? _sppClient;
    private Stream? _sppStream;
    private CancellationTokenSource? _readCts;
    private CancellationTokenSource? _inventoryCts;
    private readonly ChainwayFrameBuffer _frameBuffer = new();
    private bool _isChainway;
    private bool _disposed;

    public event Action<byte[]>? DataReceived;
    public event Action<RfidScanner.Models.RfidTag>? TagReceived;
    public event Action<string>? StatusChanged;
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected { get; private set; }
    public bool IsInventoryRunning { get; private set; }
    public bool IsChainwayDevice => _isChainway;

    public async Task<IReadOnlyList<DeviceModel>> ScanDevicesAsync(CancellationToken ct = default)
    {
        var results = new List<DeviceModel>();

        try
        {
            StatusChanged?.Invoke("Scanning BLE devices (look for Nordic_UART_CW)...");
            var bleDevices = await Bluetooth.ScanForDevicesAsync();
            foreach (var device in bleDevices)
            {
                ct.ThrowIfCancellationRequested();
                var name = device.Name ?? "Unknown";
                results.Add(new DeviceModel
                {
                    Name = name,
                    Address = device.Id,
                    IsBle = true,
                    SignalStrength = 0,
                    IsChainway = ChainwayProtocol.IsChainwayDevice(name)
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
                    SignalStrength = 0,
                    IsChainway = ChainwayProtocol.IsChainwayDevice(device.DeviceName)
                });
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"SPP scan error: {ex.Message}");
        }

        results = results
            .OrderByDescending(d => d.IsChainway)
            .ThenBy(d => d.DisplayName)
            .ToList();

        var chainwayCount = results.Count(d => d.IsChainway);
        StatusChanged?.Invoke(chainwayCount > 0
            ? $"Found {results.Count} device(s). {chainwayCount} Chainway/R6 candidate(s)."
            : $"Found {results.Count} device(s). Power on R6 and scan again if missing.");
        return results;
    }

    public async Task ConnectAsync(DeviceModel device, CancellationToken ct = default)
    {
        await DisconnectAsync();

        _isChainway = device.IsChainway || ChainwayProtocol.IsChainwayDevice(device.Name);

        if (device.IsBle)
            await ConnectBleAsync(device, ct);
        else
            await ConnectSppAsync(device, ct);
    }

    private async Task ConnectBleAsync(DeviceModel device, CancellationToken ct)
    {
        StatusChanged?.Invoke($"Connecting BLE: {device.DisplayName}...");

        _bleDevice = await FindBleDeviceAsync(device.Address, ct)
            ?? throw new InvalidOperationException("BLE device not found. Power on R6 and scan again.");

        await _bleDevice.Gatt.ConnectAsync();

        if (_isChainway || ChainwayProtocol.IsChainwayDevice(_bleDevice.Name))
        {
            _isChainway = true;
            await ConnectChainwayBleAsync(ct);
        }
        else
        {
            await ConnectGenericBleAsync(ct);
        }

        IsConnected = true;
        ConnectionChanged?.Invoke(true);
        StatusChanged?.Invoke(_isChainway
            ? $"Connected to Chainway R6: {device.DisplayName}. Click Start Scan."
            : $"Connected (BLE): {device.DisplayName}");
    }

    private async Task<BleDevice?> FindBleDeviceAsync(string address, CancellationToken ct)
    {
        var devices = await Bluetooth.ScanForDevicesAsync();
        return devices.FirstOrDefault(d => d.Id == address);
    }

    private async Task ConnectChainwayBleAsync(CancellationToken ct)
    {
        var service = await _bleDevice!.Gatt.GetPrimaryServiceAsync(ChainwayProtocol.ServiceUuid)
            ?? throw new InvalidOperationException("Nordic UART service not found. Is this a Chainway R6?");

        _writeCharacteristic = await service.GetCharacteristicAsync(ChainwayProtocol.RxUuid)
            ?? throw new InvalidOperationException("Chainway write characteristic not found.");

        _notifyCharacteristic = await service.GetCharacteristicAsync(ChainwayProtocol.TxUuid)
            ?? throw new InvalidOperationException("Chainway notify characteristic not found.");

        _notifyCharacteristic.CharacteristicValueChanged += OnBleDataReceived;
        await _notifyCharacteristic.StartNotificationsAsync();
    }

    private async Task ConnectGenericBleAsync(CancellationToken ct)
    {
        var services = await _bleDevice!.Gatt.GetPrimaryServicesAsync();
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

    public async Task StartInventoryAsync()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected to a device.");

        await StopInventoryAsync();

        if (_isChainway && _writeCharacteristic != null)
        {
            await StartChainwayInventoryAsync();
            return;
        }

        throw new InvalidOperationException("Inventory start is only implemented for Chainway BLE readers.");
    }

    private async Task StartChainwayInventoryAsync()
    {
        _frameBuffer.Clear();
        IsInventoryRunning = true;

        try
        {
            // Configure real-time inventory, then enable continuous tag streaming
            await WriteChainwayCommandAsync(ChainwayProtocol.BuildSetRealtimeParamsCommand());
            await Task.Delay(100);
            await WriteChainwayCommandAsync(ChainwayProtocol.BuildSetRealtimeModeCommand());
            await Task.Delay(100);

            _inventoryCts = new CancellationTokenSource();
            _ = Task.Run(() => InventoryFallbackLoop(_inventoryCts.Token), _inventoryCts.Token);

            StatusChanged?.Invoke("R6 inventory started — present tags near the reader.");
        }
        catch
        {
            IsInventoryRunning = false;
            throw;
        }
    }

    private async Task InventoryFallbackLoop(CancellationToken ct)
    {
        // Keep reader active with periodic inventory commands (helps some firmware builds)
        while (!ct.IsCancellationRequested && IsConnected)
        {
            try
            {
                await WriteChainwayCommandAsync(ChainwayProtocol.BuildInventoryCommand());
            }
            catch
            {
                break;
            }

            try
            {
                await Task.Delay(400, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task StopInventoryAsync()
    {
        _inventoryCts?.Cancel();
        _inventoryCts?.Dispose();
        _inventoryCts = null;

        if (_isChainway && _writeCharacteristic != null && IsInventoryRunning)
        {
            try
            {
                await WriteChainwayCommandAsync(ChainwayProtocol.BuildStopInventoryCommand());
            }
            catch { /* ignore */ }
        }

        if (IsInventoryRunning)
        {
            IsInventoryRunning = false;
            StatusChanged?.Invoke("Inventory stopped.");
        }
    }

    private async Task WriteChainwayCommandAsync(byte[] command)
    {
        if (_writeCharacteristic == null)
            throw new InvalidOperationException("Chainway write characteristic not available.");

        await _writeCharacteristic.WriteValueWithoutResponseAsync(command);
    }

    private void OnBleDataReceived(object? sender, GattCharacteristicValueChangedEventArgs e)
    {
        if (e.Value == null || e.Value.Length == 0)
            return;

        DataReceived?.Invoke(e.Value);
        ProcessIncomingData(e.Value);
    }

    private void ProcessIncomingData(byte[] chunk)
    {
        if (_isChainway)
        {
            _frameBuffer.Append(chunk);
            foreach (var frameBytes in _frameBuffer.ExtractFrames())
            {
                if (!ChainwayProtocol.TryParseFrame(frameBytes, out var frame))
                    continue;

                foreach (var tag in ChainwayProtocol.ExtractTags(frame))
                    TagReceived?.Invoke(tag);
            }
            return;
        }

        var parsed = RfidParser.Parse(chunk);
        if (parsed != null)
            TagReceived?.Invoke(parsed);
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
                    ProcessIncomingData(chunk);
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
        await StopInventoryAsync();

        _readCts?.Cancel();
        _readCts?.Dispose();
        _readCts = null;

        if (_notifyCharacteristic != null)
        {
            _notifyCharacteristic.CharacteristicValueChanged -= OnBleDataReceived;
            try { await _notifyCharacteristic.StopNotificationsAsync(); } catch { /* ignore */ }
            _notifyCharacteristic = null;
        }

        _writeCharacteristic = null;

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

        _frameBuffer.Clear();
        _isChainway = false;

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
