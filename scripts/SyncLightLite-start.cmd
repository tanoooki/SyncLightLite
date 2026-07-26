@echo off
setlocal
set "APPROOT=%~dp0.."
for %%I in ("%APPROOT%") do set "APPROOT=%%~fI"
cd /d "%APPROOT%"
if not exist "%LOCALAPPDATA%\SyncLightLite" mkdir "%LOCALAPPDATA%\SyncLightLite"
node src\live.js live --fps 30 >> "%LOCALAPPDATA%\SyncLightLite\synclightlite.log" 2>&1
