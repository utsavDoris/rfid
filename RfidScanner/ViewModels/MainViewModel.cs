using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RfidScanner.Core;
using RfidScanner.Data;
using RfidScanner.Models;

namespace RfidScanner.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private BluetoothService? _bluetooth;
    private readonly TagManager _tagManager = new();
    private readonly ScanDatabase _database = new();
    private readonly CloudSyncService _cloudSync = new();
    private DispatcherTimer? _scanDurationTimer;
    private DateTime _scanStartTime;
    private bool _disposed;

    [ObservableProperty]
    private string _statusMessage = "Ready — scan for devices.";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isInventoryRunning;

    [ObservableProperty]
    private BluetoothDeviceInfo? _selectedDevice;

    [ObservableProperty]
    private int _liveTagCount;

    [ObservableProperty]
    private int _totalScanCount;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private int _minRssiFilter = -100;

    [ObservableProperty]
    private string _apiEndpoint = string.Empty;

    [ObservableProperty]
    private bool _isCloudSyncEnabled;

    [ObservableProperty]
    private bool _isKeyboardWedgeEnabled;

    [ObservableProperty]
    private int _cloudSyncQueueSize;

    [ObservableProperty]
    private int _transmitPower = 30;

    [ObservableProperty]
    private bool _isSoundMuted;

    [ObservableProperty]
    private int _uniqueTagCount;

    [ObservableProperty]
    private int _totalReadCount;

    [ObservableProperty]
    private string _tagStatsDisplay = "Total: 0, Unique: 0";

    [ObservableProperty]
    private string _scanDurationDisplay = "0s";

    public ObservableCollection<BluetoothDeviceInfo> Devices { get; } = new();
    public ObservableCollection<RfidTag> LiveTags => _tagManager.LiveTags;
    public ObservableCollection<RfidTag> HistoryTags { get; } = new();

    public string? CurrentUserEmail => SupabaseService.Instance.CurrentUserEmail;

    public ICollectionView LiveTagsView { get; }
    public ICollectionView HistoryTagsView { get; }

    public bool ShowTagPlaceholder => !IsInventoryRunning && LiveTags.Count == 0;
    public bool ShowScanProgress => IsInventoryRunning && LiveTags.Count == 0;
    public string ConnectionStatusText => IsInventoryRunning ? "Scanning" : IsConnected ? "Connected" : "Disconnected";

    public MainViewModel()
    {
        LiveTagsView = CollectionViewSource.GetDefaultView(LiveTags);
        LiveTagsView.Filter = FilterTag;

        HistoryTagsView = CollectionViewSource.GetDefaultView(HistoryTags);
        HistoryTagsView.Filter = FilterTag;

        _cloudSync.QueueSizeChanged += size => RunOnUi(() => CloudSyncQueueSize = size);
        _cloudSync.SyncErrorOccurred += err => RunOnUi(() => StatusMessage = $"Cloud Sync Error: {err}");
        _cloudSync.Start();

        InitializeBluetooth();

        _tagManager.TagAddedOrUpdated += OnTagProcessed;
        _tagManager.NewTagDiscovered += OnNewTagDiscovered;
        _tagManager.LiveTagsChanged += () => RunOnUi(RefreshLiveStats);
        _tagManager.Start();
        LoadHistory();
    }

    private void InitializeBluetooth()
    {
        try
        {
            _bluetooth = new BluetoothService();
            _bluetooth.TagReceived += tag => _tagManager.ProcessTag(tag);
            _bluetooth.StatusChanged += msg => RunOnUi(() => StatusMessage = msg);
            _bluetooth.DeviceDiscovered += OnDeviceDiscovered;
            _bluetooth.DeviceRemoved += OnDeviceRemoved;
            _bluetooth.ScanCompleted += OnScanCompleted;
            _bluetooth.ConnectionChanged += connected => RunOnUi(() =>
            {
                IsConnected = connected;
                if (!connected)
                {
                    IsInventoryRunning = false;
                    StopScanDurationTimer();
                    StatusMessage = "Reader disconnected — select device and connect again.";
                }
                else
                {
                    StatusMessage = "R6 connected. Press Start Scan or device trigger.";
                }
                NotifyTagPanelState();
                RefreshCommands();
            });
            _bluetooth.HardwareTriggerPressed += () => RunOnUi(() => _ = ToggleInventoryFromTriggerAsync());
        }
        catch (Exception ex)
        {
            StatusMessage = $"BLE reader unavailable: {ex.Message}";
        }
    }

    private BluetoothService RequireBluetooth()
    {
        if (_bluetooth == null)
            throw new InvalidOperationException("BLE reader is not available. Check Chainway DLLs beside the executable.");
        return _bluetooth;
    }

    [RelayCommand]
    private async Task LogoutAsync(Window window)
    {
        try
        {
            if (IsConnected)
                await DisconnectReaderAsync();
        }
        catch
        {
            // Continue logout even if disconnect fails.
        }

        await SupabaseService.Instance.LogoutAsync();
        AppSession.ReturnToLogin = true;
        window.Close();
    }

    [RelayCommand]
    private void OpenProfile(Window window)
    {
        var profileWindow = new RfidScanner.Views.ProfileWindow();
        profileWindow.Owner = window;
        profileWindow.ShowDialog();
    }

    [RelayCommand]
    private void OpenUserAdmin(Window window)
    {
        var userAdminWindow = new RfidScanner.Views.UserManagementWindow();
        userAdminWindow.Owner = window;
        userAdminWindow.ShowDialog();
    }

    [RelayCommand]
    private async Task ScanDevicesAsync()
    {
        if (_bluetooth == null)
        {
            StatusMessage = "BLE reader is not available.";
            return;
        }

        if (IsConnected)
        {
            StatusMessage = "Disconnect the reader before scanning for devices.";
            return;
        }

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
        // Ignore watcher removals — they trigger Windows popup churn and false disconnects.
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
            var bluetooth = RequireBluetooth();

            if (IsScanning)
                await bluetooth.StopScanAsync();

            StatusMessage = $"Connecting to {SelectedDevice.DeviceLabel}...";
            await bluetooth.ConnectAsync(SelectedDevice);

            if (!bluetooth.IsConnected)
                return;

            var pwr = bluetooth.GetPower();
            if (pwr > 0) TransmitPower = pwr;

            ApplyReaderBeep();

            await Task.Delay(800).ConfigureAwait(true);

            if (!bluetooth.IsConnected)
            {
                StatusMessage = "Reader disconnected during connect. Try again.";
                return;
            }

            StatusMessage = "R6 connected. Press trigger on device or click Start Scan.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
            IsConnected = false;
        }
    }

    private async Task ToggleInventoryFromTriggerAsync()
    {
        if (!IsConnected)
            return;

        if (IsInventoryRunning)
            await StopInventoryAsync();
        else
            await StartInventoryAsync();
    }

    [RelayCommand(CanExecute = nameof(CanStartInventory))]
    private async Task StartInventoryAsync()
    {
        try
        {
            await RequireBluetooth().StartInventoryAsync();
            IsInventoryRunning = true;
            _scanStartTime = DateTime.Now;
            ScanDurationDisplay = "0s";
            StartScanDurationTimer();
            NotifyTagPanelState();
            StatusMessage = "Scanning — press R6 trigger or Stop Scan to end.";
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
            await RequireBluetooth().StopInventoryAsync();
            IsInventoryRunning = false;
            StopScanDurationTimer();
            NotifyTagPanelState();
            StatusMessage = "Scan stopped. Press Start Scan or device trigger to resume.";
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

    private bool CanStartInventory() => IsConnected && !IsInventoryRunning;
    private bool CanStopInventory() => IsConnected && IsInventoryRunning;

    private bool CanConnect() => SelectedDevice != null && !IsConnected && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanSetPower))]
    private void ApplyPower()
    {
        var bluetooth = RequireBluetooth();
        if (bluetooth.SetPower(TransmitPower))
        {
            StatusMessage = $"Transmit power set to {TransmitPower} dBm.";
        }
        else
        {
            StatusMessage = "Failed to set transmit power.";
            var actual = bluetooth.GetPower();
            if (actual > 0) TransmitPower = actual;
        }
    }

    private bool CanSetPower() => IsConnected && !IsInventoryRunning;

    [RelayCommand(CanExecute = nameof(CanToggleSound))]
    private void ToggleSound()
    {
        IsSoundMuted = !IsSoundMuted;
    }

    private bool CanToggleSound() => IsConnected;

    partial void OnIsSoundMutedChanged(bool value) => ApplyReaderBeep();

    private void ApplyReaderBeep()
    {
        if (!IsConnected || _bluetooth == null)
            return;

        if (_bluetooth.SetBeep(!IsSoundMuted))
            StatusMessage = IsSoundMuted ? "Reader beep muted." : "Reader beep unmuted.";
        else
            StatusMessage = "Could not change reader beep setting.";
    }

    public async Task DisconnectReaderAsync()
    {
        if (!IsConnected || _bluetooth == null)
            return;

        if (IsInventoryRunning)
            await _bluetooth.StopInventoryAsync();

        await _bluetooth.DisconnectAsync();
        IsInventoryRunning = false;
        IsConnected = false;
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        await DisconnectReaderAsync();
        StatusMessage = "Disconnected.";
        RefreshCommands();
    }

    private bool CanDisconnect() => IsConnected;

    [RelayCommand]
    private void ClearLiveTags()
    {
        _tagManager.Clear();
        RefreshLiveStats();
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
    partial void OnIsConnectedChanged(bool value)
    {
        RefreshCommands();
        NotifyTagPanelState();
    }

    partial void OnIsInventoryRunningChanged(bool value)
    {
        RefreshCommands();
        NotifyTagPanelState();
    }

    private void NotifyTagPanelState()
    {
        OnPropertyChanged(nameof(ShowTagPlaceholder));
        OnPropertyChanged(nameof(ShowScanProgress));
        OnPropertyChanged(nameof(ConnectionStatusText));
    }

    partial void OnFilterTextChanged(string value)
    {
        RunOnUi(() => 
        {
            LiveTagsView.Refresh();
            HistoryTagsView.Refresh();
        });
    }

    partial void OnMinRssiFilterChanged(int value)
    {
        RunOnUi(() => 
        {
            LiveTagsView.Refresh();
            HistoryTagsView.Refresh();
        });
    }

    partial void OnApiEndpointChanged(string value) => _cloudSync.ApiEndpoint = value;
    partial void OnIsCloudSyncEnabledChanged(bool value) => _cloudSync.IsSyncEnabled = value;

    private bool FilterTag(object obj)
    {
        if (obj is not RfidTag tag) return false;

        if (tag.Rssi < MinRssiFilter) return false;

        if (string.IsNullOrWhiteSpace(FilterText)) return true;

        var text = FilterText.Trim();
        bool matches = false;

        try 
        {
            var regex = new Regex(text, RegexOptions.IgnoreCase);
            if (!string.IsNullOrEmpty(tag.Epc) && regex.IsMatch(tag.Epc)) matches = true;
            if (!string.IsNullOrEmpty(tag.Tid) && regex.IsMatch(tag.Tid)) matches = true;
        }
        catch 
        {
            var lowerText = text.ToLowerInvariant();
            if (!string.IsNullOrEmpty(tag.Epc) && tag.Epc.ToLowerInvariant().Contains(lowerText)) matches = true;
            if (!string.IsNullOrEmpty(tag.Tid) && tag.Tid.ToLowerInvariant().Contains(lowerText)) matches = true;
        }

        return matches;
    }

    private void OnTagProcessed(RfidTag tag)
    {
        RunOnUi(() =>
        {
            _database.SaveScan(tag);
            TotalScanCount++;
            RefreshLiveStats();

            var existing = HistoryTags.FirstOrDefault(h => h.UniqueKey == tag.UniqueKey);
            if (existing != null)
                HistoryTags.Remove(existing);

            HistoryTags.Insert(0, tag);

            while (HistoryTags.Count > 500)
                HistoryTags.RemoveAt(HistoryTags.Count - 1);

            if (IsCloudSyncEnabled)
                _cloudSync.EnqueueTag(tag);
        });
    }

    private void RefreshLiveStats()
    {
        UniqueTagCount = LiveTags.Count;
        TotalReadCount = LiveTags.Sum(t => t.ReadCount);
        LiveTagCount = UniqueTagCount;
        TagStatsDisplay = $"Total: {TotalReadCount}, Unique: {UniqueTagCount}";
        NotifyTagPanelState();
    }

    private void StartScanDurationTimer()
    {
        _scanDurationTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _scanDurationTimer.Tick -= OnScanDurationTick;
        _scanDurationTimer.Tick += OnScanDurationTick;
        _scanDurationTimer.Start();
    }

    private void StopScanDurationTimer()
    {
        _scanDurationTimer?.Stop();
    }

    private void OnScanDurationTick(object? sender, EventArgs e)
    {
        if (!IsInventoryRunning)
            return;

        var seconds = (int)(DateTime.Now - _scanStartTime).TotalSeconds;
        ScanDurationDisplay = $"{seconds}s";
        RefreshLiveStats();
    }

    private void OnNewTagDiscovered(RfidTag tag)
    {
        if (IsKeyboardWedgeEnabled)
        {
            RunOnUi(() =>
            {
                try
                {
                    System.Windows.Forms.SendKeys.SendWait(tag.UniqueKey + "{ENTER}");
                }
                catch { /* ignore */ }
            });
        }
    }

    private void LoadHistory()
    {
        try
        {
            foreach (var tag in _database.GetHistory())
                HistoryTags.Add(tag);

            TotalScanCount = HistoryTags.Count;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load history: {ex.Message}";
        }
    }

    private void RefreshCommands()
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        StartInventoryCommand.NotifyCanExecuteChanged();
        StopInventoryCommand.NotifyCanExecuteChanged();
        ApplyPowerCommand.NotifyCanExecuteChanged();
        ToggleSoundCommand.NotifyCanExecuteChanged();
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

        StopScanDurationTimer();
        _tagManager.Stop();
        _cloudSync.Stop();
        _bluetooth?.Dispose();
        _database.Dispose();
        _cloudSync.Dispose();
    }
}
