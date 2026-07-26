const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
for (const rel of ['SyncLightLite-start.cmd', 'SyncLightLite-off.cmd', 'SyncLightLite-tray.cmd', 'install-startup.cmd', 'uninstall-startup.cmd']) {
  const scriptPath = path.join(root, 'scripts', rel);
  assert(fs.existsSync(scriptPath), `${rel} should exist`);
}
const start = fs.readFileSync(path.join(root, 'scripts/SyncLightLite-start.cmd'), 'utf8');
const tray = fs.readFileSync(path.join(root, 'scripts/SyncLightLite-tray.cmd'), 'utf8');
const off = fs.readFileSync(path.join(root, 'scripts/SyncLightLite-off.cmd'), 'utf8');
const install = fs.readFileSync(path.join(root, 'scripts/install-startup.cmd'), 'utf8');
const uninstall = fs.readFileSync(path.join(root, 'scripts/uninstall-startup.cmd'), 'utf8');
assert(start.replaceAll('\\', '/').includes('node src/live.js live --fps 30'), 'legacy start script should run continuous live at 30 fps via PATH');
assert(tray.replaceAll('\\', '/').includes('dist/tray/SyncLightLite.exe'), 'tray script should launch tray exe');
assert(off.replaceAll('\\', '/').includes('node src/live.js off'), 'off script should turn LEDs off via PATH');
assert(install.replaceAll('\\', '/').includes('set "STARTUP=%PROGRAMS%/Startup"'), 'install should use Startup folder for non-admin tray autostart');
assert(install.includes('SyncLightLite.lnk'), 'install should create root Start Menu .lnk shortcut');
assert(install.includes('CreateShortcut'), 'install should create real Windows shortcuts, not only .cmd files');
assert(install.replaceAll('\\', '/').includes('dist/tray/SyncLightLite.exe'), 'Start Menu shortcut should point to tray exe');
assert(install.replaceAll('\\', '/').includes('%STARTUP%/SyncLightLite.lnk') || install.includes("Join-Path $Startup 'SyncLightLite.lnk'"), 'Startup entry should be a .lnk so Task Manager shows the app icon/name');
assert(!install.includes('copy /Y "%APPROOT%\\scripts\\SyncLightLite-tray.cmd"'), 'install should not place SyncLightLite.cmd in Startup');
assert(uninstall.includes('SyncLightLite.lnk'), 'uninstall should remove Startup .lnk entries');
assert(install.includes('SyncLightLite-Off-On-Shutdown'), 'install should register shutdown/off task');
assert(!install.includes('/SC ONLOGON'), 'install should not use elevated ONLOGON schtasks');
