# RFID Bluetooth Scanner — C# / WPF / .NET 8

A Windows desktop app to connect to a Bluetooth RFID reader and display scanned tags in real time.

---

## Requirements

- Windows 10 (19041+) or Windows 11
- .NET 8 SDK → https://dotnet.microsoft.com/download
- A Bluetooth-enabled RFID reader (BLE or SPP/Classic)

---

## Quick start

```bash
cd RfidScanner
dotnet restore
dotnet run
```

**No hardware?** Click **▶ Start Simulation** — the app generates fake tags so you can explore the UI.

---

## Project structure

```
RfidScanner/
├── Core/
│   ├── BluetoothService.cs   ← BLE discovery + SPP connection
│   ├── RfidParser.cs         ← Raw hex → RfidTag (3 frame formats)
│   └── TagManager.cs         ← Dedup, live list, 30 s purge
├── Models/
│   ├── RfidTag.cs
│   └── BluetoothDeviceInfo.cs
├── Data/
│   └── ScanDatabase.cs       ← SQLite storage + CSV export
├── ViewModels/
│   └── MainViewModel.cs      ← MVVM wiring
├── Views/
│   └── MainWindow.xaml       ← Dark-mode WPF UI
└── Converters/
    └── RssiToColorConverter.cs
```

---

## Adapting the parser to your reader

Open `Core/RfidParser.cs`. The `Parse()` method handles three common frame formats:

| Format | Header   | Example use           |
|--------|----------|-----------------------|
| A      | `AA BB`  | Generic cheap readers |
| B      | `A0`     | Chainway, Zebra       |
| C      | Raw hex  | No framing at all     |

Check your reader's communication protocol PDF and adjust `ParseFormatA` or `ParseFormatB` to match the actual byte layout.

---

## Adapting the Bluetooth connection

In `Core/BluetoothService.cs`:

- **BLE readers**: The code looks for the first `Notify` characteristic. If your reader uses a specific service UUID, replace the loop with:
  ```csharp
  var svc = await gatt.GetPrimaryServiceAsync(Guid.Parse("YOUR-SERVICE-UUID"));
  var ch  = await svc.GetCharacteristicAsync(Guid.Parse("YOUR-CHAR-UUID"));
  ```

- **SPP readers** (most common): Pair the device in Windows Bluetooth settings first, then connect. The SPP path uses `InTheHand.Bluetooth.BluetoothClient`.

---

## CSV export

Click **Export CSV** in the Scan History panel. The file saves to the app's working directory.
Format: `TagId, TagType, Rssi, ReadCount, ScannedAt`

---

## NuGet packages used

| Package                    | Purpose                  |
|----------------------------|--------------------------|
| InTheHand.BluetoothLE      | BLE + SPP Bluetooth API  |
| CommunityToolkit.Mvvm      | ObservableObject, RelayCommand |
| Microsoft.Data.Sqlite      | Local scan database      |
| Newtonsoft.Json            | JSON utilities           |