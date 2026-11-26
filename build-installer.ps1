$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " DroidDeck Windows Desktop & Installer   " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# 1. Build native C# desktop app + stage bundled scrcpy/adb
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'desktop\build.ps1')
if ($LASTEXITCODE -ne 0) {
  throw "Desktop build failed with exit code $LASTEXITCODE"
}

# 2. Locate Inno Setup 6 compiler (ISCC.exe)
$isccCandidates = @(
  (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
  'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
  'C:\Program Files\Inno Setup 6\ISCC.exe'
)

$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
  $cmd = Get-Command 'iscc' -ErrorAction SilentlyContinue
  if ($cmd) { $iscc = $cmd.Source }
}

if (-not $iscc) {
  throw "Inno Setup 6 (ISCC.exe) not found. Install via: winget install JRSoftware.InnoSetup"
}

Write-Host "[DroidDeck] Packaging installer with ISCC: $iscc" -ForegroundColor Cyan
$issPath = Join-Path $root 'installer\DroidDeck.iss'
& $iscc $issPath
if ($LASTEXITCODE -ne 0) {
  throw "ISCC failed with exit code $LASTEXITCODE"
}

$setupExe = Join-Path $root 'dist\DroidDeck-Setup-1.0.0.exe'
if (-not (Test-Path $setupExe)) {
  throw "Expected installer output not found: $setupExe"
}

$info = Get-Item $setupExe
$mb = [Math]::Round($info.Length / 1MB, 2)
Write-Host "[DroidDeck] Installer ready: $($info.FullName) ($mb MB)" -ForegroundColor Green
