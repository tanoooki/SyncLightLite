const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const tray = fs.readFileSync(path.join(root, 'tray/SyncLightLite.Tray/Program.cs'), 'utf8');

assert(tray.includes('TabControl'), 'settings UI should use tabs instead of one long vertical form');
assert(tray.includes('TabPage'), 'settings UI should split controls into tab pages');
for (const key of ['tabGeneral', 'tabEffects', 'tabCapture', 'tabColor']) {
  assert(tray.includes(key), `settings UI should include ${key} tab translation`);
}
assert(tray.includes('BuildTabPage'), 'settings UI should have reusable tab/page builder');
assert(/new TabPage\(title\).*AutoScroll = true/.test(tray), 'each tab should scroll when space is limited, leaving the save footer fixed');
