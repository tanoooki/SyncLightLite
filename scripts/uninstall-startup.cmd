@echo off
setlocal
set "PROGRAMS=%APPDATA%\Microsoft\Windows\Start Menu\Programs"
del "%PROGRAMS%\Startup\SyncLightLite.lnk" 2>nul
del "%PROGRAMS%\Startup\SyncLightLite.cmd" 2>nul
del "%PROGRAMS%\Startup\SyncLightLite-start.cmd" 2>nul
del "%PROGRAMS%\SyncLightLite.lnk" 2>nul
rmdir /S /Q "%PROGRAMS%\SyncLightLite" 2>nul
schtasks /Delete /TN "SyncLightLite-Off-On-Shutdown" /F
echo SyncLightLite scheduled tasks, Startup shortcut, and Start Menu entries removed.
