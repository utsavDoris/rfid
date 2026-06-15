using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RfidScanner.Core;
using RfidScanner.Models;

namespace RfidScanner.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private BluetoothService? _bluetooth;
    private readonly TagManager _tagManager = new();
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
    private int _transmitPower = 30;

    [ObservableProperty]
    private bool _isSoundMuted = true;

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

    public string? CurrentUserEmail => SupabaseService.Instance.CurrentUserEmail;

    public ICollectionView LiveTagsView { get; }

    public bool ShowTagPlaceholder => !IsInventoryRunning && LiveTags.Count == 0;
    public bool ShowScanProgress => IsInventoryRunning && LiveTags.Count == 0;
    public string ConnectionStatusText => IsInventoryRunning ? "Scanning" : IsConnected ? "Connected" : "Disconnected";

    public MainViewModel()
    {
        LiveTagsView = CollectionViewSource.GetDefaultView(LiveTags);

        InitializeBluetooth();

        _tagManager.TagAddedOrUpdated += _ => RunOnUi(RefreshLiveStats);
        _tagManager.LiveTagsChanged += () => RunOnUi(RefreshLiveStats);
        _tagManager.Start();
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
                    StatusMessage = "Reader disconnected — scan list kept. Reconnect to scan again.";
                }
                else
                {
                    StatusMessage = "R6 connected. Press Start Scan or device trigger.";
                }
                NotifyTagPanelState();
                RefreshCommands();
            });
            _bluetooth.HardwareTriggerPressed += OnHardwareTriggerPressed;
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
            {
                await bluetooth.StopScanAsync();
                IsScanning = false;
                await Task.Delay(500).ConfigureAwait(true);
            }

            StatusMessage = $"Connecting to {SelectedDevice.DeviceLabel} (direct BLE — do not pair in Windows)...";
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

    private void OnHardwareTriggerPressed()
    {
        if (_disposed || !IsConnected)
            return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;

        dispatcher.BeginInvoke(new Action(() => _ = ToggleInventoryFromTriggerAsync()), DispatcherPriority.Normal);
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
        StatusMessage = "Disconnected from app.";
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
            if (LiveTags.Count == 0)
            {
                StatusMessage = "No tags to export.";
                MessageBox.Show("No scanned tags to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"rfid_scan_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("Tag,Count,RSSI");
            foreach (var tag in LiveTags.OrderByDescending(t => t.LastSeen))
            {
                sb.AppendLine(string.Join(",",
                    EscapeCsv(tag.TidDisplay),
                    tag.ReadCount.ToString(),
                    EscapeCsv(tag.RssiDisplay)));
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            StatusMessage = $"Exported {LiveTags.Count} unique tag(s) to CSV.";
            MessageBox.Show($"Exported to:\n{path}\n\nOpen in Excel or any spreadsheet app.", "Export CSV", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
            MessageBox.Show($"Export failed:\n{ex.Message}", "Export CSV", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
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

    private void RefreshLiveStats()
    {
        UniqueTagCount = LiveTags.Count;
        TotalReadCount = LiveTags.Sum(t => t.ReadCount);
        LiveTagCount = UniqueTagCount;
        TagStatsDisplay = $"Unique: {UniqueTagCount}, Scanned: {TotalReadCount}";
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

        try
        {
            _bluetooth?.ShutdownReader();
        }
        catch
        {
            // Continue shutdown even if reader cleanup fails.
        }

        _bluetooth?.Dispose();
    }
}
