using System.Windows.Controls;
using System.Windows.Input;
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
        if (DataContext is not MainViewModel vm || vm.IsConnected || vm.IsConnecting)
            return;

        if (sender is not ListBox listBox)
            return;

        var element = e.OriginalSource as System.Windows.DependencyObject;
        if (element == null)
            return;

        var container = listBox.ContainerFromElement(element) as ListBoxItem;
        if (container?.DataContext is not BluetoothDeviceInfo device)
            return;

        listBox.SelectedItem = device;
        if (vm.ConnectToDeviceCommand.CanExecute(device))
            vm.ConnectToDeviceCommand.Execute(device);
    }
}
