$ErrorActionPreference = "Stop"

$desktopDir = $PSScriptRoot
$rootDir = Split-Path -Parent $desktopDir
$buildDir = Join-Path $desktopDir "build"
$outExe = Join-Path $buildDir "DroidDeck.exe"

$csc = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
}

$fxDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"

New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

# Ensure scrcpy + adb exist before bundling
$scrcpySrc = Join-Path $rootDir "bin\scrcpy"
if (-not (Test-Path (Join-Path $scrcpySrc "adb.exe"))) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $rootDir "scripts\setup-scrcpy.ps1")
}

$sources = @(
    (Join-Path $desktopDir "Program.cs"),
    (Join-Path $desktopDir "Models\KeymapModels.cs"),
    (Join-Path $desktopDir "Core\NativeMethods.cs"),
    (Join-Path $desktopDir "Core\AdbManager.cs"),
    (Join-Path $desktopDir "Core\ScrcpyControlChannel.cs"),
    (Join-Path $desktopDir "Core\ScrcpyHost.cs"),
    (Join-Path $desktopDir "Core\InputEngine.cs"),
    (Join-Path $desktopDir "UI\OverlayForm.cs"),
    (Join-Path $desktopDir "UI\KeyBindDialog.cs"),
    (Join-Path $desktopDir "UI\MainForm.cs")
)

$cscArgs = @(
    "/nologo",
    "/target:winexe",
    "/platform:x64",
    "/optimize+",
    "/out:$outExe",
    "/reference:$fxDir\System.dll",
    "/reference:$fxDir\System.Core.dll",
    "/reference:$fxDir\System.Drawing.dll",
    "/reference:$fxDir\System.Windows.Forms.dll",
    "/reference:$fxDir\System.Web.Extensions.dll"
) + $sources

Write-Host "[DroidDeck] Compiling native desktop binary..."
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Compilation failed!"
    exit 1
}

# Copy bundled scrcpy + adb alongside DroidDeck.exe for standalone run & installer packaging
$scrcpyDst = Join-Path $buildDir "bin\scrcpy"
New-Item -ItemType Directory -Force -Path $scrcpyDst | Out-Null
Copy-Item -Path (Join-Path $scrcpySrc "*") -Destination $scrcpyDst -Recurse -Force

Write-Host "[DroidDeck] Build succeeded: $outExe"
Get-Item $outExe | Select-Object Name, Length, LastWriteTime
