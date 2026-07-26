@echo off
setlocal
set "APPROOT=%~dp0.."
for %%I in ("%APPROOT%") do set "APPROOT=%%~fI"
start "SyncLightLite" "%APPROOT%\dist\tray\SyncLightLite.exe" --minimized
