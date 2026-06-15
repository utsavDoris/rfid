# Build Release exe folder (no zip) - output: publish\AcurisDesktop
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "RfidScanner\RfidScanner.csproj"
$buildDir = Join-Path $root "RfidScanner\bin\Release\net48"
$outDir = Join-Path $root "publish\AcurisDesktop"

Stop-Process -Name RfidScanner -Force -ErrorAction SilentlyContinue
Stop-Process -Name AcurisDesktop -Force -ErrorAction SilentlyContinue

Write-Host "Building Release..."
dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $buildDir "AcurisDesktop.exe"
if (-not (Test-Path $exe)) {
    Write-Error "AcurisDesktop.exe not found in $buildDir"
    exit 1
}

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

# Copy only top-level release files (no stale win-x86 subfolders)
Get-ChildItem $buildDir -File | Copy-Item -Destination $outDir -Force

Write-Host ""
Write-Host "Public exe folder ready:"
Write-Host "  $outDir"
Write-Host "  Run: $(Join-Path $outDir 'AcurisDesktop.exe')"
