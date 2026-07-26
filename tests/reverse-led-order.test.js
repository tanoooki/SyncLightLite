const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const { frameToLedColors } = require(path.join(root, 'src/mapper.js'));

function makeFrame(width, height) {
  const frame = Buffer.alloc(width * height * 3);
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const idx = (y * width + x) * 3;
    if (x >= width - 6) frame[idx] = 255;          // right edge red
    else if (y < 6) frame[idx + 1] = 255;          // top edge green
    else if (x < 6) frame[idx + 2] = 255;          // left edge blue
  }
  return frame;
}

const normal = frameToLedColors(makeFrame(256, 108), 256, 108, 71);
const reversed = frameToLedColors(makeFrame(256, 108), 256, 108, 71, { reverseLedOrder: true });

assert.strictEqual(reversed.length, normal.length, 'reversed mapping should preserve LED count');
for (let i = 0; i < normal.length; i++) {
  assert.deepStrictEqual(reversed[i], normal[normal.length - 1 - i], `LED ${i + 1} should map to the opposite physical end when reversed`);
}

const live = fs.readFileSync(path.join(root, 'src/live.js'), 'utf8');
const tray = fs.readFileSync(path.join(root, 'tray/SyncLightLite.Tray/Program.cs'), 'utf8');
const config = fs.readFileSync(path.join(root, 'config.json'), 'utf8');
const readme = fs.readFileSync(path.join(root, 'README.md'), 'utf8');

assert(live.includes('reverseLedOrder'), 'live engine should read reverseLedOrder from config/CLI');
assert(live.includes('raw.ReverseLedOrder'), 'config normalizer should accept PascalCase ReverseLedOrder from .NET UI');
assert(tray.includes('ReverseLedOrder'), 'tray config model should persist ReverseLedOrder');
assert(tray.includes('Inverter ordem dos LEDs'), 'settings UI should expose an LED order toggle');
assert(tray.includes('TabControl'), 'settings UI should use tabbed sections so lower controls are not clipped');
assert(tray.includes('(T("invertStrip"), _reverseLedOrder'), 'reverse LED order should be near the top of the settings UI');
assert(config.includes('reverseLedOrder'), 'default config should include reverseLedOrder');
assert(readme.includes('reverseLedOrder'), 'README should document reverseLedOrder');
