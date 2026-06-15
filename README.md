# Acuris Desktop (RfidScanner)

WPF desktop app for Chainway R6 BLE RFID scanning, inventory, and Supabase auth.

## Requirements

| Tool | Notes |
|------|--------|
| Windows 10/11 | x86 app (runs on 64-bit Windows) |
| [.NET SDK](https://dotnet.microsoft.com/download) | To build from source |
| [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48) | Required to **run** the exe |
| Bluetooth | For R6 reader (do **not** pair R6 in Windows Bluetooth settings) |

## Run from source (development)

```powershell
cd RfidScanner
dotnet run
```

Stop with `Ctrl+C`, then run again after code changes.

## Generate public exe folder

Builds **Release** and copies the full output (exe + DLLs) to `publish\RfidScanner`.  
**No zip** — share the whole `publish\RfidScanner` folder.

```powershell
# From repo root (E:\rfid)
powershell -ExecutionPolicy Bypass -File .\build-exe.ps1
```

**Output:**

```
publish\RfidScanner\RfidScanner.exe
```

Run the public build:

```powershell
.\publish\RfidScanner\RfidScanner.exe
```

### Manual build (without script)

```powershell
cd RfidScanner
dotnet build -c Release
```

Exe and dependencies:

```
RfidScanner\bin\Release\net48\RfidScanner.exe
```

Copy the entire `net48` folder to another PC. Do not copy only the exe — Chainway BLE DLLs must stay in the same folder.

## Scanner usage

1. Login (session is saved locally).
2. Open **RFID Scanner**.
3. **Scan Devices** → select R6 → **Connect** (or tap device in list).
4. **Start Scan** or use the R6 hardware trigger.
5. **Disconnect** when finished.

## Project layout

| Path | Purpose |
|------|---------|
| `RfidScanner/` | WPF app (UI, ViewModels, Supabase) |
| `RfidScanner.ChainwayBridge/` | Chainway BLE bridge + WinForms message pump |
| `publish/RfidScanner/` | Generated release folder (gitignored) |
| `build-exe.ps1` | Build script for public exe folder |

## Troubleshooting

- **Exe won't start** — Install .NET Framework 4.8.
- **BLE / connect fails** — Use in-app scan only; remove R6 from Windows paired devices.
- **Build locked** — Close running `RfidScanner.exe`, then rebuild.
