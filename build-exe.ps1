# Build Release exe folder (no zip) — copy full output beside RfidScanner.exe
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "RfidScanner\RfidScanner.csproj"
$buildDir = Join-Path $root "RfidScanner\bin\Release\net48"
$outDir = Join-Path $root "publish\RfidScanner"

Stop-Process -Name RfidScanner -Force -ErrorAction SilentlyContinue
Stop-Process -Name AcurisDesktop -Force -ErrorAction SilentlyContinue

Write-Host "Building Release..."
dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $buildDir "RfidScanner.exe"
if (-not (Test-Path $exe)) {
    Write-Error "RfidScanner.exe not found in $buildDir"
    exit 1
}

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
Copy-Item "$buildDir\*" $outDir -Recurse -Force

Write-Host ""
Write-Host "Exe folder ready:"
Write-Host "  $outDir"
Write-Host "  Run: $exe"
Write-Host "  Public copy: $(Join-Path $outDir 'RfidScanner.exe')"
