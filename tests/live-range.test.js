const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const src = fs.readFileSync(path.join(root, 'src/live.js'), 'utf8');

assert(src.includes("cmd === 'range'"), 'live.js should expose a range command');
assert(src.includes('rangeData(start, end, rgb[0], rgb[1], rgb[2], leds)'), 'range command should build SC range data');
assert(src.includes('syncScreenPacket'), 'range command should use SC sync packets');
