using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RfidScanner.Core;

namespace RfidScanner.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public DashboardViewModel(ShellViewModel shell)
    {
        _shell = shell;
        QuickTiles = new ObservableCollection<DashboardTile>
        {
            new("scanner", "RFID Scanner", "Connect R6 and scan tags", "Phase 4 — Done"),
            new("products", "Product Management", "View stock and inventory", "Phase 5 — In progress"),
            new("sell", "Sell", "Mark items as sold", "Phase 6"),
            new("return", "Returns", "Process returns", "Phase 7"),
            new("memo", "Memo", "Consignment workflow", "Phase 8"),
            new("collections", "In/Out Tracker", "Track collections", "Phase 9"),
            new("bulkupload", "Bulk Upload", "Excel import", "Phase 10"),
            new("reports", "Reports", "Scan reports", "Phase 11")
        };
    }

    public string? UserName => SupabaseService.Instance.CurrentUserProfile?.UserName;
    public string? CompanyName => SupabaseService.Instance.CurrentUserProfile?.CompanyName;
    public string? RoleName => SupabaseService.Instance.CurrentUserProfile?.RoleName;

    public ObservableCollection<DashboardTile> QuickTiles { get; }

    [RelayCommand]
    private void OpenTile(DashboardTile tile) => _shell.NavigateTo(tile.NavId);
}

public class DashboardTile
{
    public DashboardTile(string navId, string title, string subtitle, string phase)
    {
        NavId = navId;
        Title = title;
        Subtitle = subtitle;
        Phase = phase;
    }

    public string NavId { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Phase { get; }
}
