using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RfidScanner.Core;
using RfidScanner.Views;

namespace RfidScanner;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                args.Exception.Message,
                "Application Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (!TryInitializeSupabase())
            return;

        RunSessionLoop();
    }

    private bool TryInitializeSupabase()
    {
        try
        {
            var initTask = Task.Run(() => SupabaseService.Instance.InitializeAsync());
            if (!initTask.Wait(TimeSpan.FromSeconds(20)))
            {
                MessageBox.Show(
                    "Could not connect to the server in time.\nCheck your internet connection and try again.",
                    "Startup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Shutdown();
                return false;
            }

            initTask.GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Startup failed:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return false;
        }
    }

    /// <summary>Login → Main → (logout → login again | close app)</summary>
    private void RunSessionLoop()
    {
        while (true)
        {
            var loginWindow = new LoginWindow();
            if (loginWindow.ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            if (!RunMainSession())
            {
                Shutdown();
                return;
            }

            if (!AppSession.ReturnToLogin)
            {
                Shutdown();
                return;
            }
        }
    }

    private bool RunMainSession()
    {
        AppSession.ReturnToLogin = false;

        try
        {
            var mainWindow = new ShellWindow();
            MainWindow = mainWindow;
            mainWindow.ShowDialog();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to open main window:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }
}
