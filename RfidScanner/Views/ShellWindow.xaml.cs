using System;
using System.Windows;
using System.Windows.Controls;
using RfidScanner.ViewModels;

namespace RfidScanner.Views;

public partial class ShellWindow : Window
{
    public ShellWindow()
    {
        InitializeComponent();

        try
        {
            DataContext = new ShellViewModel();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to load application:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }

        Closed += OnClosed;
    }

    private void NavList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ShellViewModel vm && NavList.SelectedItem is NavMenuItem item)
            vm.NavigateTo(item.Id);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is IDisposable disposable)
            disposable.Dispose();
    }
}
