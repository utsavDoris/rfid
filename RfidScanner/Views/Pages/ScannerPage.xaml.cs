using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RfidScanner.Models;
using RfidScanner.ViewModels;

namespace RfidScanner.Views.Pages;

public partial class ScannerPage : UserControl
{
    public ScannerPage()
    {
        InitializeComponent();
    }

    private void DeviceListBox_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsConnecting)
            return;

        if (FindDeviceFromClick(sender, e) is not BluetoothDeviceInfo device)
            return;

        DeviceListBox.SelectedItem = device;
        vm.ConnectToDeviceCommand.Execute(device);
    }

    private static BluetoothDeviceInfo? FindDeviceFromClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox)
            return null;

        var element = e.OriginalSource as DependencyObject;
        while (element != null && element is not ListBoxItem)
            element = VisualTreeHelper.GetParent(element);

        if (element is ListBoxItem { DataContext: BluetoothDeviceInfo device })
            return device;

        // Fallback: hit-test when visual tree walk misses (virtualized / template edge cases).
        var hit = VisualTreeHelper.HitTest(listBox, e.GetPosition(listBox))?.VisualHit;
        while (hit != null && hit is not ListBoxItem)
            hit = VisualTreeHelper.GetParent(hit);

        return hit is ListBoxItem { DataContext: BluetoothDeviceInfo hitDevice } ? hitDevice : null;
    }
}
