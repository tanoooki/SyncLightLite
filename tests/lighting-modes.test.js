const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const live = fs.readFileSync(path.join(root, 'src/live.js'), 'utf8');
const tray = fs.readFileSync(path.join(root, 'tray/SyncLightLite.Tray/Program.cs'), 'utf8');
const config = fs.readFileSync(path.join(root, 'config.json'), 'utf8');
const pkg = require(path.join(root, 'package.json'));

for (const mode of ['screen', 'solid', 'breathing', 'rainbow', 'colorCycle', 'wave']) {
  assert(config.includes(`"mode": "screen"`) || mode !== 'screen', 'default config should include screen mode');
  assert(live.includes(mode), `live engine should support ${mode} mode`);
  assert(tray.includes(mode), `tray UI should expose ${mode} mode`);
}
assert(config.includes('effectColor'), 'config should include effectColor for fixed/breathing modes');
assert(config.includes('effectSpeedPercent'), 'config should include effectSpeedPercent');
assert(tray.includes('_mode'), 'settings UI should include mode selector');
assert(tray.includes('_effectColor'), 'settings UI should include color selector');
assert(tray.includes('_effectSpeed'), 'settings UI should include effect speed control');
assert(live.includes('runEffect'), 'live should dispatch non-screen effects');
assert(live.includes('hsvToRgb'), 'live should generate rainbow/color cycle colors');
assert(live.includes('solidPacket') && live.includes('colorsPacket'), 'effects should use solid and per-LED packets');
assert(pkg.scripts.test.includes('lighting-modes.test.js'), 'canonical npm test should run lighting modes test');
