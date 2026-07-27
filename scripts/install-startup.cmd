@echo off
setlocal
if not exist "%LOCALAPPDATA%\SyncLightLite" mkdir "%LOCALAPPDATA%\SyncLightLite"
set "APPROOT=%~dp0.."
for %%I in ("%APPROOT%") do set "APPROOT=%%~fI"
set "PROGRAMS=%APPDATA%\Microsoft\Windows\Start Menu\Programs"
set "APPFOLDER=%PROGRAMS%\SyncLightLite"
set "STARTUP=%PROGRAMS%\Startup"
set "APPICON=%APPROOT%\assets\SyncLightLite.ico"
if not exist "%APPFOLDER%" mkdir "%APPFOLDER%"
del "%STARTUP%\SyncLightLite-start.cmd" 2>nul
del "%STARTUP%\SyncLightLite.cmd" 2>nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%PROGRAMS%\SyncLightLite.lnk'); $s.TargetPath='%APPROOT%\dist\tray\SyncLightLite.exe'; $s.WorkingDirectory='%APPROOT%'; $s.Description='SyncLightLite'; $s.IconLocation='%APPICON%'; $s.Save()"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%APPFOLDER%\SyncLightLite.lnk'); $s.TargetPath='%APPROOT%\dist\tray\SyncLightLite.exe'; $s.WorkingDirectory='%APPROOT%'; $s.Description='SyncLightLite'; $s.IconLocation='%APPICON%'; $s.Save()"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%APPFOLDER%\Apagar luz.lnk'); $s.TargetPath='%APPROOT%\scripts\SyncLightLite-off.cmd'; $s.WorkingDirectory='%APPROOT%'; $s.Description='Apagar luz SyncLightLite'; $s.IconLocation='%APPICON%'; $s.Save()"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%STARTUP%\SyncLightLite.lnk'); $s.TargetPath='%APPROOT%\dist\tray\SyncLightLite.exe'; $s.Arguments='--minimized'; $s.WorkingDirectory='%APPROOT%'; $s.Description='SyncLightLite startup'; $s.IconLocation='%APPICON%'; $s.Save()"
schtasks /Delete /TN "SyncLightLite-Off-On-Shutdown" /F >nul 2>nul

echo SyncLightLite tray startup shortcut and Start Menu shortcuts installed. Shutdown/off is handled by the tray app.
