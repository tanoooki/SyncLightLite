const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const src = fs.readFileSync(path.join(root, 'src/live.js'), 'utf8');
const tray = fs.readFileSync(path.join(root, 'tray/SyncLightLite.Tray/Program.cs'), 'utf8');

assert(src.includes('config.fps ?? 30'), 'live mode should default to config fps or 30');
assert(src.includes('fps: Number(raw.fps ?? raw.Fps ?? 30)'), 'normalizer should default missing fps to 30');
assert(tray.includes('public int Fps { get; set; } = 30;'), 'tray config model should default fps to 30');
