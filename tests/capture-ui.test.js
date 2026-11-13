const path = require('path');
const fs = require('fs');
const assert = require('assert');
const projectText = fs.readFileSync(path.join(__dirname, '../tray/SyncLightLite.Tray/SyncLightLite.Tray.csproj'), 'utf8');
assert(projectText.includes('<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>'), 'Desktop guides need physical primary-monitor coordinates under mixed DPI');
const { execFileSync } = require('child_process');
execFileSync('dotnet', ['build', path.join(__dirname, 'CaptureHarness/CaptureHarness.csproj'), '--nologo', '-v:q'], { stdio: 'inherit' });
execFileSync('dotnet', [path.join(__dirname, 'CaptureHarness/bin/Debug/net9.0-windows/CaptureHarness.dll'), 'ui'], { stdio: 'inherit' });
