$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$rootDir = Split-Path -Parent $PSScriptRoot
$binDir = Join-Path $rootDir "bin"
$scrcpyDir = Join-Path $binDir "scrcpy"
$adbExe = Join-Path $scrcpyDir "adb.exe"
$scrcpyExe = Join-Path $scrcpyDir "scrcpy.exe"

if ((Test-Path $adbExe) -and (Test-Path $scrcpyExe)) {
    Write-Host "[DroidDeck] Scrcpy & ADB are already installed at $scrcpyDir"
    exit 0
}

New-Item -ItemType Directory -Force -Path $binDir | Out-Null
$zipPath = Join-Path $binDir "scrcpy-win64.zip"

$downloadUrl = "https://github.com/Genymobile/scrcpy/releases/download/v5.0.1/scrcpy-win64-v5.0.1.zip"
try {
    Write-Host "[DroidDeck] Checking latest Genymobile/scrcpy release..."
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Genymobile/scrcpy/releases/latest" -TimeoutSec 10
    $asset = $release.assets | Where-Object { $_.name -match "win64.*\.zip$" } | Select-Object -First 1
    if ($asset -and $asset.browser_download_url) {
        $downloadUrl = $asset.browser_download_url
        Write-Host "[DroidDeck] Found $($release.tag_name): $downloadUrl"
    }
} catch {
    Write-Host "[DroidDeck] GitHub API check failed, falling back to default release URL."
}

Write-Host "[DroidDeck] Downloading Scrcpy + ADB..."
Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath

Write-Host "[DroidDeck] Extracting to $scrcpyDir..."
Expand-Archive -Path $zipPath -DestinationPath $binDir -Force
$extracted = Get-ChildItem $binDir -Directory | Where-Object { $_.Name -like "scrcpy-win64*" } | Select-Object -First 1
if ($extracted) {
    if (Test-Path $scrcpyDir) { Remove-Item $scrcpyDir -Recurse -Force }
    Rename-Item -Path $extracted.FullName -NewName "scrcpy"
}
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue

if ((Test-Path $adbExe) -and (Test-Path $scrcpyExe)) {
    Write-Host "[DroidDeck] Setup complete!"
} else {
    Write-Error "[DroidDeck] Extraction failed: adb.exe or scrcpy.exe not found."
    exit 1
}
