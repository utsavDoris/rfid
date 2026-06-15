# Acuris Desktop (RfidScanner)

WPF desktop app for Chainway R6 BLE RFID scanning, inventory, and Supabase auth.

## Requirements

| Tool | Notes |
|------|--------|
| Windows 10/11 | x86 app (runs on 64-bit Windows) |
| [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48) | Required on every PC |
| Bluetooth | For R6 reader (do not pair R6 in Windows Bluetooth) |
| [.NET SDK](https://dotnet.microsoft.com/download) | Build from source only |
| [Inno Setup 6](https://jrsoftware.org/isdl.php) | Build Setup.exe only |

---

## Run from source (development)

```powershell
cd RfidScanner
dotnet run
```

---

## Generate Setup.exe (public installer)

From the **repo root** (`E:\rfid` or wherever you cloned the project):

```powershell
cd E:\rfid
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
```

This script:

1. Builds Release → `publish\AcurisDesktop\` (exe + DLLs)
2. Compiles Inno Setup → `publish\Setup.exe`

**Output file:**

```
E:\rfid\publish\Setup.exe
```

**Verify it was created:**

```powershell
Get-Item .\publish\Setup.exe | Select-Object FullName, Length, LastWriteTime
```

Upload `Setup.exe` to [GitHub Releases](https://github.com/utsavDoris/rfid/releases).  
End users double-click Setup.exe to install (Start Menu + Desktop shortcut + uninstall in Settings).

### Inno Setup (required once)

Install [Inno Setup 6](https://jrsoftware.org/isdl.php) if `build-installer.ps1` cannot find `ISCC.exe`:

```powershell
winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
```

The build script also tries winget automatically. It looks for `ISCC.exe` in:

- `C:\Program Files (x86)\Inno Setup 6\`
- `C:\Program Files\Inno Setup 6\`
- `%LOCALAPPDATA%\Programs\Inno Setup 6\`

### Build exe folder only (no Setup.exe)

If you only need the portable folder, not the installer:

```powershell
cd E:\rfid
powershell -ExecutionPolicy Bypass -File .\build-exe.ps1
```

Output: `publish\AcurisDesktop\AcurisDesktop.exe` (+ DLLs)

---

## End user install

1. Download `Setup.exe` from GitHub Releases.
2. Double-click **Setup.exe**.
3. Follow the wizard (requires admin).
4. Launch **Acuris Desktop** from Start Menu or Desktop.

Uninstall: **Settings > Apps > Acuris Desktop**

---

## Scanner usage

1. Login (session saved locally).
2. Open **RFID Scanner**.
3. **Scan Devices** -> select R6 -> **Connect**.
4. **Start Scan** or use R6 hardware trigger.
5. **Disconnect** when finished.

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Setup.exe won't build | Install [Inno Setup 6](https://jrsoftware.org/isdl.php) or run `winget install --id JRSoftware.InnoSetup -e` |
| App won't start | Install [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48) |
| BLE connect fails | Scan in app only; unpair R6 in Windows Bluetooth |
| Build locked | Close AcurisDesktop.exe, rebuild |
