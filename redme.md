# RFID Bluetooth Scanner — Chainway R6 / WPF

Windows desktop app to connect to a **Chainway R6** Bluetooth RFID reader and display scanned tags in real time.  
Uses the official **win_ble_V1.2** SDK (`BLEDeviceAPI.dll`) — same flow as `UHF_BLE.exe`.

---

## Requirements

| Item | Details |
|------|---------|
| OS | Windows 10 (19041+) or Windows 11 |
| .NET | .NET SDK (8 or 10) — app targets **.NET Framework 4.8** |
| Platform | **x86** (32-bit) — required by Chainway `BLEAPI1.dll` / `BLEAPI2.dll` |
| Hardware | Chainway R6 (BLE name usually `Nordic_UART_CW`) |
| Bluetooth | PC Bluetooth ON; R6 powered on |

Official demo reference (working):

```
win_ble_V1.2/app/UHF_BLE.exe
win_ble_V1.2/sound code/   ← source (MainForm.cs, InventoryForm.cs)
```

---

## Quick start (run app)

Open **PowerShell**:

```powershell
cd E:\rfid\RfidScanner
$env:DOTNET_SKIP_WORKLOAD_INTEGRATION='1'
& "C:\Program Files\dotnet\dotnet.exe" run
```

Or run the built `.exe` directly:

```powershell
E:\rfid\RfidScanner\bin\Debug\net48\RfidScanner.exe
```

**No reader?** Click **▶ Start Simulation** to test the UI with fake tags.

---

## Build commands

### Build main app

```powershell
cd E:\rfid\RfidScanner
$env:DOTNET_SKIP_WORKLOAD_INTEGRATION='1'
& "C:\Program Files\dotnet\dotnet.exe" restore
& "C:\Program Files\dotnet\dotnet.exe" build
```

### Build bridge library only

```powershell
$env:DOTNET_SKIP_WORKLOAD_INTEGRATION='1'
& "C:\Program Files\dotnet\dotnet.exe" build E:\rfid\RfidScanner.ChainwayBridge\RfidScanner.ChainwayBridge.csproj
```

### Release build

```powershell
cd E:\rfid\RfidScanner
$env:DOTNET_SKIP_WORKLOAD_INTEGRATION='1'
& "C:\Program Files\dotnet\dotnet.exe" build -c Release
```

Release output:

```
E:\rfid\RfidScanner\bin\Release\net48\RfidScanner.exe
```

### Close app before rebuild

If build fails with **"file is being used by another process"**, close the running app first:

```powershell
Stop-Process -Name RfidScanner -Force -ErrorAction SilentlyContinue
```

Then build again.

---

## How to use (same as official UHF_BLE demo)

1. Power on the **R6** reader.
2. Turn on **Bluetooth** on your PC.
3. Click **Scan Devices** — wait for devices to appear (look for `Nordic_UART_CW`).
4. Click **Stop Scan** to stop early, or wait until scan completes.
5. Select the R6 device in the list.
6. Click **Connect** — inventory starts automatically after connect.
7. Hold R6 near RFID tags — EPCs appear in **Live Tags**.
8. Click **■ Stop Scan** to stop tag reading.
9. Click **Disconnect** when done.

---

## Project structure

```
rfid/
├── RfidScanner/                    ← WPF app (net48, x86)
│   ├── Core/
│   │   ├── BluetoothService.cs     ← Wraps Chainway bridge
│   │   └── TagManager.cs           ← Live tag list, dedup, purge
│   ├── ViewModels/MainViewModel.cs
│   ├── Views/MainWindow.xaml
│   └── Libs/                       ← Chainway DLLs (copied from win_ble_V1.2/app)
│       ├── BLEDeviceAPI.dll
│       ├── BLEAPI1.dll
│       ├── BLEAPI2.dll
│       ├── Common.dll
│       └── WindowsFormsControl.dll
│
└── RfidScanner.ChainwayBridge/     ← net48 x86 bridge to official SDK
    └── ChainwayReader.cs           ← Scan / Connect / Inventory (like MainForm.cs)
```

Build output must contain these DLLs **next to** `RfidScanner.exe`:

```
bin\Debug\net48\
├── RfidScanner.exe
├── BLEDeviceAPI.dll
├── BLEAPI1.dll
├── BLEAPI2.dll
├── Common.dll
├── WindowsFormsControl.dll
└── RfidScanner.ChainwayBridge.dll
```

---

## Troubleshooting

### `dotnet` is not recognized

Use the full path:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" --version
```

If that fails, install .NET SDK: https://dotnet.microsoft.com/download

---

### SDK workload error on build

```powershell
$env:DOTNET_SKIP_WORKLOAD_INTEGRATION='1'
& "C:\Program Files\dotnet\dotnet.exe" build E:\rfid\RfidScanner\RfidScanner.csproj
```

---

### Warning NU1702 (.NET Framework vs .NET 10)

```
ProjectReference was resolved using .NETFramework,Version=v4.8 instead of .NETCoreApp...
```

This is **OK** — the Chainway bridge must be **net48 x86**. The app is also **net48** now, so this warning should not appear after a clean build.

---

### `PlatformNotSupportedException` on startup

```
Operation is not supported on this platform. (0x80131539)
at BLEDeviceAPI.RFIDWithUHFBEL.GetInstance()
```

**Cause:** App was running on .NET 10 / wrong platform. Chainway SDK only works on **.NET Framework 4.8 x86**.

**Fix:** Use current project (`TargetFramework=net48`, `PlatformTarget=x86`) and run:

```powershell
E:\rfid\RfidScanner\bin\Debug\net48\RfidScanner.exe
```

Do **not** run from old `net10.0-windows` output folder.

---

### Scan shows no devices

**Checks:**

1. Confirm official demo works on same PC:
   ```powershell
   & "C:\Users\GT\Downloads\Bluetooth Connection to Windows PC 1\R2 R6 Bluetooth Connection to Windows PC\win_ble_V1.2\win_ble_V1.2\win_ble_V1.2\app\UHF_BLE.exe"
   ```
2. R6 is powered on and in range.
3. PC Bluetooth is enabled.
4. Click **Scan Devices** and wait 10–20 seconds.
5. Click **Stop Scan** if needed.

**Note:** BLE scan runs on the **UI thread** (same as official `MainForm.cs`). Devices appear live in the list.

---

### Connection failed

- Select a device from the scan list (do not type MAC manually).
- App uses full Windows BLE **Device Id** (same as official demo `SubItems[2]`).
- Only one app can connect to R6 at a time — close `UHF_BLE.exe` if it is open.

---

### Build: DLL file locked

```powershell
Stop-Process -Name RfidScanner -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
& "C:\Program Files\dotnet\dotnet.exe" build E:\rfid\RfidScanner\RfidScanner.csproj
```

---

### Error popup / red status message

When scan, connect, or inventory fails:

- A **popup** shows the error message.
- Status bar turns **red**.
- Click **Clear** next to the status bar to reset.

---

### Copy Chainway DLLs from official package

If DLLs are missing, copy from `win_ble_V1.2/app` to project `Libs` folders:

```powershell
$src = "C:\Users\GT\Downloads\Bluetooth Connection to Windows PC 1\R2 R6 Bluetooth Connection to Windows PC\win_ble_V1.2\win_ble_V1.2\win_ble_V1.2\app"
Copy-Item "$src\*.dll" "E:\rfid\RfidScanner\Libs\" -Force
Copy-Item "$src\*.dll" "E:\rfid\RfidScanner.ChainwayBridge\Libs\" -Force
```

Then rebuild.

---

## Visual Studio / Cursor

1. Open `E:\rfid\RfidScanner\RfidScanner.sln` or `RfidScanner.csproj`
2. Set **RfidScanner** as startup project
3. Platform: **x86** (if shown)
4. Press **F5** (Debug) or **Ctrl+F5** (Run without debugging)

---

## CSV export

Click **Export CSV** in Scan History. File saves to the app working directory.

Format: `TagId, TagType, Rssi, ReadCount, ScannedAt`

---

## NuGet packages

| Package | Purpose |
|---------|---------|
| CommunityToolkit.Mvvm | MVVM (`ObservableObject`, `RelayCommand`) |
| Microsoft.Data.Sqlite | Local scan database |
| Newtonsoft.Json | JSON utilities |

Chainway BLE is **not** a NuGet package — it uses DLLs in `Libs/` from `win_ble_V1.2/app`.

---

## Command cheat sheet

```powershell
# Go to project
cd E:\rfid\RfidScanner

# Set env (avoid SDK workload errors)
$env:DOTNET_SKIP_WORKLOAD_INTEGRATION='1'

# Restore + build
& "C:\Program Files\dotnet\dotnet.exe" restore
& "C:\Program Files\dotnet\dotnet.exe" build

# Run
& "C:\Program Files\dotnet\dotnet.exe" run

# Run exe directly
.\bin\Debug\net48\RfidScanner.exe

# Kill stuck app
Stop-Process -Name RfidScanner -Force -ErrorAction SilentlyContinue

# Release build
& "C:\Program Files\dotnet\dotnet.exe" build -c Release
```
