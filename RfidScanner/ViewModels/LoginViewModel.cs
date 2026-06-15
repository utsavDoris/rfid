using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RfidScanner.Core;

namespace RfidScanner.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly SupabaseService _supabase = SupabaseService.Instance;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [RelayCommand]
    private async Task LoginAsync(Window window)
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Email is required.";
            return;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            ErrorMessage = "Enter a valid email address.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Password is required.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        var normalizedEmail = Email.Trim().ToLowerInvariant();
        var normalizedPassword = Password.Trim();

        var result = await _supabase.LoginAsync(normalizedEmail, normalizedPassword).ConfigureAwait(true);

        IsBusy = false;

        if (result.Success)
        {
            window.DialogResult = true;
        }
        else
        {
            ErrorMessage = result.ErrorMessage;
        }
    }
}
