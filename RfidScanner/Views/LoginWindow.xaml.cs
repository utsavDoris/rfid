using System.ComponentModel;
using System.Windows;
using RfidScanner.ViewModels;

namespace RfidScanner.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    private void TxtPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm)
            vm.Password = TxtPassword.Password;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DialogResult == null)
            DialogResult = false;

        base.OnClosing(e);
    }
}
