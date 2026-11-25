@echo off
chcp 65001 >nul
title DroidDeck — Type-C 极速投屏 ^& 键鼠映射中心
cd /d "%~dp0"

if not exist "bin\scrcpy\adb.exe" (
    echo [DroidDeck] 首次运行，正在自动下载 Scrcpy ^& ADB 核心组件...
    powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\setup-scrcpy.ps1"
    if errorlevel 1 (
        echo [DroidDeck] 自动下载失败，请检查网络后重试。
        pause
        exit /b 1
    )
)

set ADB_LIBUSB=1
set ADB_BURST_MODE=0
set ADB_MDNS_AUTO_CONNECT=0
echo ============================================================
echo   DroidDeck — Type-C 极速投屏 ^& 键鼠映射中心已启动
echo   控制台地址: http://localhost:3080
echo ============================================================
start "" "http://localhost:3080"
node server.js
pause
