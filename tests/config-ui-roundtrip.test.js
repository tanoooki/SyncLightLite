const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const live = fs.readFileSync(path.join(root, 'src/live.js'), 'utf8');
const tray = fs.readFileSync(path.join(root, 'tray/SyncLightLite.Tray/Program.cs'), 'utf8');

assert(live.includes('normalizeConfig'), 'live should normalize config property casing from tray UI');
assert(live.includes('raw.CaptureScalePercent'), 'live should read PascalCase CaptureScalePercent written by .NET UI');
assert(live.includes('raw.HeaderOffsetPercent'), 'live should read PascalCase HeaderOffsetPercent written by .NET UI');
assert(live.includes('raw.Fps'), 'live should read PascalCase Fps written by .NET UI');
assert(live.includes('Math.max(8'), 'live interval should allow up to about 120 fps');
assert(tray.includes('Maximum = 120'), 'tray FPS control should allow 120 fps');
