using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RfidScanner.Core;

namespace RfidScanner.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly SupabaseService _supabase = SupabaseService.Instance;

    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _companyName = string.Empty;

    [ObservableProperty]
    private string _roleName = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ProfileViewModel()
    {
        var profile = _supabase.CurrentUserProfile;
        if (profile != null)
        {
            UserName = profile.UserName;
            Email = profile.Email;
            CompanyName = profile.CompanyName;
            RoleName = profile.RoleName;
        }
    }

    [RelayCommand]
    private async Task SaveProfileAsync(Window window)
    {
        var profile = _supabase.CurrentUserProfile;
        if (profile == null) return;

        profile.UserName = UserName;
        
        try
        {
            await _supabase.Client.From<Models.UserModel>()
                .Where(x => x.Id == profile.Id)
                .Set(x => x.UserName, profile.UserName)
                .Update();

            StatusMessage = "Profile updated successfully!";
        }
        catch (System.Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }
}
