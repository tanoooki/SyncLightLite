const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
for (const rel of ['SyncLightLite-start.cmd', 'SyncLightLite-off.cmd', 'install-startup.cmd']) {
  const content = fs.readFileSync(path.join(root, 'scripts', rel), 'utf8');
  assert(!content.includes('C:\Program Files\nodejs\node.exe'), `${rel} should not use unquoted Program Files node path`);
}
const start = fs.readFileSync(path.join(root, 'scripts/SyncLightLite-start.cmd'), 'utf8');
const off = fs.readFileSync(path.join(root, 'scripts/SyncLightLite-off.cmd'), 'utf8');
assert(start.replaceAll('\\', '/').includes('node src/live.js live --fps 30'), 'start script should invoke node via PATH');
assert(off.replaceAll('\\', '/').includes('node src/live.js off'), 'off script should invoke node via PATH');
const install = fs.readFileSync(path.join(root, 'scripts/install-startup.cmd'), 'utf8');
assert(install.replaceAll('\\', '/').includes('set "STARTUP=%PROGRAMS%/Startup"'), 'install should use Startup folder for tray logon autostart');
assert(install.includes('SyncLightLite.lnk'), 'install should create searchable Start Menu shortcut');
assert(!install.includes('/SC ONLOGON'), 'install should not try elevated ONLOGON schtasks');
