namespace RfidScanner.Models;

public class BluetoothDeviceInfo
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Mac { get; set; } = string.Empty;
    public bool IsBle { get; set; }
    public bool IsChainway { get; set; }
    public int SignalStrength { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Address : Name;
    public string ConnectionType => IsBle ? "BLE" : "SPP";
    public string DeviceLabel => IsChainway ? $"{DisplayName} (R6)" : DisplayName;
}
