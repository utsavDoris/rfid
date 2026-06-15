using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RfidScanner.Core;
using RfidScanner.Models;

namespace RfidScanner.ViewModels;

public partial class UserManagementViewModel : ObservableObject
{
    private readonly SupabaseService _supabase = SupabaseService.Instance;

    [ObservableProperty]
    private ObservableCollection<UserModel> _users = new();

    [ObservableProperty]
    private UserModel? _selectedUser;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public UserManagementViewModel()
    {
        _ = LoadUsersAsync();
    }

    [RelayCommand]
    private async Task LoadUsersAsync()
    {
        if (_supabase.CurrentUserProfile == null) return;

        IsLoading = true;
        ErrorMessage = string.Empty;

        var company = _supabase.CurrentUserProfile.CompanyName;
        var users = await _supabase.GetCompanyUsersAsync(company);
        
        Users.Clear();
        foreach (var u in users)
        {
            Users.Add(u);
        }

        IsLoading = false;
    }

    [RelayCommand]
    private async Task DeleteUserAsync(UserModel? user)
    {
        if (user == null) return;
        
        // Soft delete logic would go here
        // await _supabase.Client.From<UserModel>().Where(x => x.Id == user.Id).Update(new { IsDeleted = true });
        
        await LoadUsersAsync();
    }
}
