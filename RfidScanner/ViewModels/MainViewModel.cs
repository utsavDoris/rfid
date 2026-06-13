using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RfidScanner.Core;
using RfidScanner.Data;
using RfidScanner.Models;

namespace RfidScanner.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly BluetoothService _bluetooth = new();
    private readonly TagManager _tagManager = new();
    private readonly ScanDatabase _database = new();
    private readonly Random _random = new();
    private Timer? _simulationTimer;
    private bool _disposed;

    [ObservableProperty]
    private string _statusMessage = "Ready — scan for devices or start simulation.";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isSimulating;

    [ObservableProperty]
    private bool _isInventoryRunning;

    [ObservableProperty]
    private BluetoothDeviceInfo? _selectedDevice;

    [ObservableProperty]
    private int _liveTagCount;

    [ObservableProperty]
    private int _totalScanCount;

    public ObservableCollection<BluetoothDeviceInfo> Devices { get; } = new();
    public ObservableCollection<RfidTag> LiveTags => _tagManager.LiveTags;
    public ObservableCollection<RfidTag> HistoryTags { get; } = new();

    public MainViewModel()
    {
        _bluetooth.TagReceived += tag => _tagManager.ProcessTag(tag);
        _bluetooth.StatusChanged += msg => RunOnUi(() => StatusMessage = msg);
        _bluetooth.DeviceDiscovered += OnDeviceDiscovered;
        _bluetooth.DeviceRemoved += OnDeviceRemoved;
        _bluetooth.ScanCompleted += OnScanCompleted;
        _bluetooth.ConnectionChanged += connected => RunOnUi(() =>
        {
            IsConnected = connected;
            if (!connected)
                IsInventoryRunning = false;
        });
        _tagManager.TagAddedOrUpdated += OnTagProcessed;
        _tagManager.Start();
        LoadHistory();
    }

    [RelayCommand]
    private async Task ScanDevicesAsync()
    {
        if (IsScanning)
        {
            await _bluetooth.StopScanAsync();
            return;
        }

        try
        {
            IsScanning = true;
            StatusMessage = "Scanning for Bluetooth devices...";
            Devices.Clear();
            await _bluetooth.BeginScanAsync();
        }
        catch (Exception ex)
        {
            IsScanning = false;
            StatusMessage = $"Scan failed: {ex.Message}";
        }
    }

    private void OnDeviceDiscovered(BluetoothDeviceInfo device)
    {
        RunOnUi(() =>
        {
            if (Devices.All(d => d.Mac != device.Mac))
                Devices.Add(device);
        });
    }

    private void OnDeviceRemoved(string deviceId)
    {
        RunOnUi(() =>
        {
            var existing = Devices.FirstOrDefault(d => d.Address == deviceId);
            if (existing != null)
                Devices.Remove(existing);
        });
    }

    private void OnScanCompleted()
    {
        RunOnUi(() =>
        {
            IsScanning = false;
            if (Devices.Count > 0)
            {
                StatusMessage = $"Found {Devices.Count} device(s). Select one and connect.";
                if (SelectedDevice == null)
                    SelectedDevice = Devices.FirstOrDefault(d => d.IsChainway) ?? Devices.FirstOrDefault();
            }
            else
            {
                StatusMessage = "No devices found. Power on R6 and ensure Bluetooth is enabled.";
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (SelectedDevice == null) return;

        try
        {
            StatusMessage = $"Connecting to {SelectedDevice.DeviceLabel}...";
            await _bluetooth.ConnectAsync(SelectedDevice);

            if (_bluetooth.IsChainwayDevice)
            {
                StatusMessage = "R6 connected. Starting inventory...";
                await StartInventoryAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
            IsConnected = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartInventory))]
    private async Task StartInventoryAsync()
    {
        try
        {
            await _bluetooth.StartInventoryAsync();
            IsInventoryRunning = true;
            StatusMessage = "Scanning for RFID tags — hold R6 near tags.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Start scan failed: {ex.Message}";
            IsInventoryRunning = false;
        }
        finally
        {
            RefreshCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopInventory))]
    private async Task StopInventoryAsync()
    {
        try
        {
            await _bluetooth.StopInventoryAsync();
            IsInventoryRunning = false;
            StatusMessage = "Scan stopped.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Stop scan failed: {ex.Message}";
        }
        finally
        {
            RefreshCommands();
        }
    }

    private bool CanStartInventory() => IsConnected && !IsInventoryRunning && !IsSimulating;
    private bool CanStopInventory() => IsConnected && IsInventoryRunning;

    private bool CanConnect() => SelectedDevice != null && !IsConnected && !IsScanning && !IsSimulating;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        await _bluetooth.DisconnectAsync();
        IsInventoryRunning = false;
        StatusMessage = "Disconnected.";
        RefreshCommands();
    }

    private bool CanDisconnect() => IsConnected;

    [RelayCommand]
    private void StartSimulation()
    {
        if (IsSimulating) return;

        IsSimulating = true;
        StatusMessage = "Simulation running — generating fake RFID tags.";
        _simulationTimer = new Timer(GenerateSimulatedTag, null, 0, 800);
        RefreshCommands();
    }

    [RelayCommand]
    private void StopSimulation()
    {
        if (!IsSimulating) return;

        _simulationTimer?.Dispose();
        _simulationTimer = null;
        IsSimulating = false;
        StatusMessage = "Simulation stopped.";
        RefreshCommands();
    }

    [RelayCommand]
    private void ClearLiveTags()
    {
        _tagManager.Clear();
        LiveTagCount = 0;
        StatusMessage = "Live tag list cleared.";
    }

    [RelayCommand]
    private void ExportCsv()
    {
        try
        {
            var path = _database.ExportCsv();
            StatusMessage = $"Exported to {path}";
            MessageBox.Show($"Scan history exported to:\n{path}", "Export CSV", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
            MessageBox.Show($"Export failed:\n{ex.Message}", "Export CSV", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ClearHistory()
    {
        _database.ClearHistory();
        HistoryTags.Clear();
        TotalScanCount = 0;
        StatusMessage = "Scan history cleared.";
    }

    partial void OnSelectedDeviceChanged(BluetoothDeviceInfo? value) => RefreshCommands();
    partial void OnIsScanningChanged(bool value) => RefreshCommands();
    partial void OnIsConnectedChanged(bool value) => RefreshCommands();
    partial void OnIsSimulatingChanged(bool value) => RefreshCommands();
    partial void OnIsInventoryRunningChanged(bool value) => RefreshCommands();

    private void OnTagProcessed(RfidTag tag)
    {
        RunOnUi(() =>
        {
            _database.SaveScan(tag);
            LiveTagCount = LiveTags.Count;
            TotalScanCount++;

            var existing = HistoryTags.FirstOrDefault(h => h.UniqueKey == tag.UniqueKey);
            if (existing != null)
                HistoryTags.Remove(existing);

            HistoryTags.Insert(0, tag);

            while (HistoryTags.Count > 500)
                HistoryTags.RemoveAt(HistoryTags.Count - 1);
        });
    }

    private void GenerateSimulatedTag(object? state)
    {
        var epc = new byte[12];
        var tid = new byte[12];
        _random.NextBytes(epc);
        _random.NextBytes(tid);

        var tag = RfidTagMapper.FromScanned(
            BitConverter.ToString(epc).Replace("-", ""),
            BitConverter.ToString(tid).Replace("-", ""),
            string.Empty,
            $"{_random.Next(-90, -30)}");
        tag.TagType = "TID";

        _tagManager.ProcessTag(tag);
    }

    private void LoadHistory()
    {
        foreach (var tag in _database.GetHistory())
            HistoryTags.Add(tag);

        TotalScanCount = HistoryTags.Count;
    }

    private void RefreshCommands()
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        StartInventoryCommand.NotifyCanExecuteChanged();
        StopInventoryCommand.NotifyCanExecuteChanged();
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _simulationTimer?.Dispose();
        _tagManager.Stop();
        _bluetooth.Dispose();
        _database.Dispose();
    }
}
