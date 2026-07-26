const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const src = fs.readFileSync(path.join(root, 'src/live.js'), 'utf8');

assert(src.includes("cmd === 'off'"), 'live.js should expose an off command');
assert(src.includes("argValue('--duration', '0')"), 'live mode should default to continuous duration');
assert(src.includes('installShutdownHandlers'), 'live mode should install shutdown handlers');
assert(src.includes('solidPacket(0, 0, 0, leds)'), 'shutdown/off should send lights-off packet');
assert(src.includes("process.on('SIGINT'"), 'should handle Ctrl+C');
assert(src.includes("process.on('SIGTERM'"), 'should handle termination');
