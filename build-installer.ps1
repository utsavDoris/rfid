# Build publish\Setup.exe (Inno Setup) - no zip
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$iss = Join-Path $root "installer\AcurisDesktop.iss"

function Find-Iscc {
    $paths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($p in $paths) {
        if (Test-Path $p) { return $p }
    }
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd -and (Test-Path $cmd.Source)) { return $cmd.Source }
    return $null
}

function Install-InnoSetup {
    Write-Host "Inno Setup 6 not found. Installing via winget..."
    winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        Write-Host "winget install failed. Install manually: https://jrsoftware.org/isdl.php"
        return $null
    }
    return Find-Iscc
}

# 1. Build publish\AcurisDesktop
& (Join-Path $root "build-exe.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 2. Compile installer
$iscc = Find-Iscc
if (-not $iscc) { $iscc = Install-InnoSetup }
if (-not $iscc) {
    Write-Host ""
    Write-Host "Cannot build Setup.exe without Inno Setup 6."
    Write-Host "Download: https://jrsoftware.org/isdl.php"
    Write-Host "Then run: powershell -ExecutionPolicy Bypass -File build-installer.ps1"
    exit 1
}

Write-Host ""
Write-Host "Compiling Setup.exe with Inno Setup..."
& $iscc $iss
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$setup = Join-Path $root "publish\Setup.exe"
if (-not (Test-Path $setup)) {
    Write-Error "Setup.exe was not created."
    exit 1
}

Write-Host ""
Write-Host "Setup.exe ready (upload to GitHub Releases):"
Write-Host "  $setup"
$sizeMb = [math]::Round((Get-Item $setup).Length / 1MB, 2)
Write-Host "  Size: ${sizeMb} MB"
