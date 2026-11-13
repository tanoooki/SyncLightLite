const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const mapper = fs.readFileSync(path.join(root, 'src/mapper.js'), 'utf8');
const live = fs.readFileSync(path.join(root, 'src/live.js'), 'utf8');
const tray = fs.readFileSync(path.join(root, 'tray/SyncLightLite.Tray/Program.cs'), 'utf8');

for (const token of ['brightness', 'saturation', 'gamma', 'threshold', 'horizontalMarginPercent', 'blackFloor', 'live heartbeat']) {
  assert(live.includes(token), `live should use ${token}`);
}
assert(mapper.includes('processColors'), 'mapper should expose color post-processing');
assert(mapper.includes('colorDistance'), 'mapper should expose color distance for threshold');
for (const label of ['Brilho', 'Saturação', 'Gamma', 'Threshold', 'Margem horizontal', 'Piso preto', 'Perfil']) {
  assert(tray.includes(label), `tray UI should include ${label}`);
}
assert(tray.includes('FromIconImage'), 'tray should use bundled icon');
assert(tray.includes('Color.FromArgb(247, 239, 219)'), 'tray settings should use warm cream pixel-inspired design');
