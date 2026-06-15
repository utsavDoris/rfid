using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RfidScanner.Core;
using RfidScanner.Views;

namespace RfidScanner.ViewModels;

public partial class ShellViewModel : ObservableObject, IDisposable
{
    private MainViewModel? _scannerViewModel;
    private bool _disposed;

    [ObservableProperty]
    private ObservableObject? _currentPage;

    [ObservableProperty]
    private string _selectedNavId = "dashboard";

    [ObservableProperty]
    private string _pageTitle = "Dashboard";

    public string? CurrentUserEmail => SupabaseService.Instance.CurrentUserEmail;
    public string? CompanyName => SupabaseService.Instance.CurrentUserProfile?.CompanyName;

    public ObservableCollection<NavMenuItem> NavItems { get; } = new()
    {
        new NavMenuItem("dashboard", "Dashboard", "Home overview and quick actions"),
        new NavMenuItem("scanner", "RFID Scanner", "Chainway R6 BLE scanning"),
        new NavMenuItem("products", "Product Management", "Stock list and inventory"),
        new NavMenuItem("sell", "Sell", "Mark products as sold"),
        new NavMenuItem("return", "Returns", "Process product returns"),
        new NavMenuItem("memo", "Memo", "Consignment workflow"),
        new NavMenuItem("collections", "In/Out Tracker", "Track product collections"),
        new NavMenuItem("bulkupload", "Bulk Upload", "Excel / sheet import"),
        new NavMenuItem("reports", "Reports", "Scan and inventory reports"),
        new NavMenuItem("settings", "Settings", "Labels, locations, reader config")
    };

    public ShellViewModel()
    {
        NavigateTo("dashboard");
    }

    [RelayCommand]
    private void Navigate(NavMenuItem? item)
    {
        if (item != null)
            NavigateTo(item.Id);
    }

    public void NavigateTo(string navId)
    {
        SelectedNavId = navId;

        CurrentPage = navId switch
        {
            "dashboard" => CreateDashboard(),
            "scanner" => GetScannerViewModel(),
            "products" => CreateStock(),
            "sell" => CreatePlaceholder("Sell", "Scan RFID tags and mark products as sold.", "Phase 6"),
            "return" => CreatePlaceholder("Returns", "Scan and process returned items.", "Phase 7"),
            "memo" => CreatePlaceholder("Memo", "Consignment memo management.", "Phase 8"),
            "collections" => CreatePlaceholder("In/Out Tracker", "Create and track product collections.", "Phase 9"),
            "bulkupload" => CreatePlaceholder("Bulk Upload", "Import products from Excel or Google Sheets.", "Phase 10"),
            "reports" => CreatePlaceholder("Reports", "Inventory scan reports and analytics.", "Phase 11"),
            "settings" => CreatePlaceholder("Settings", "Manage labels, locations, and reader settings.", "Phase 12"),
            _ => CreateDashboard()
        };

        PageTitle = NavItems.FirstOrDefault(n => n.Id == navId)?.Title ?? "Acuris Desktop";
    }

    private DashboardViewModel CreateDashboard() => new DashboardViewModel(this);

    private StockViewModel CreateStock() => new StockViewModel();

    private ModulePlaceholderViewModel CreatePlaceholder(string title, string description, string phase) =>
        new ModulePlaceholderViewModel(title, description, phase);

    private MainViewModel GetScannerViewModel()
    {
        _scannerViewModel ??= new MainViewModel();
        return _scannerViewModel;
    }

    [RelayCommand]
    private void OpenProfile(Window window)
    {
        var profileWindow = new ProfileWindow { Owner = window };
        profileWindow.ShowDialog();
    }

    [RelayCommand]
    private void OpenUserAdmin(Window window)
    {
        var userAdminWindow = new UserManagementWindow { Owner = window };
        userAdminWindow.ShowDialog();
    }

    [RelayCommand]
    private async Task LogoutAsync(Window window)
    {
        try
        {
            if (_scannerViewModel?.IsConnected == true)
                await _scannerViewModel.DisconnectReaderAsync();
        }
        catch
        {
            // Continue logout even if disconnect fails.
        }

        await SupabaseService.Instance.LogoutAsync();
        AppSession.ReturnToLogin = true;
        window.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scannerViewModel?.Dispose();
    }
}

public class NavMenuItem
{
    public NavMenuItem(string id, string title, string description)
    {
        Id = id;
        Title = title;
        Description = description;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
}
