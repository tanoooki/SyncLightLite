const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const src = fs.readFileSync(path.join(root, 'src/live.js'), 'utf8');

assert(src.includes("cmd === 'off'"), 'live.js should expose an off command');
assert(src.includes("argValue('--duration', '0')"), 'live mode should default to continuous duration');
assert(src.includes('installShutdownHandlers'), 'live mode should install shutdown handlers');
assert(src.includes('installStdinShutdownHandler'), 'live mode should accept graceful stdin shutdown from tray');
assert(src.includes('solidPacket(0, 0, 0, leds)'), 'shutdown/off should send lights-off packet');
assert(src.includes("process.on('SIGINT'"), 'should handle Ctrl+C');
assert(src.includes("process.on('SIGTERM'"), 'should handle termination');
assert(src.includes("['off', 'shutdown', 'stop-off'].includes(command)"), 'stdin off command should stop and turn LEDs off');
assert(src.includes('stop(true);'), 'stdin off command must pass turnLightsOff=true to the live stop handler');
assert(src.includes("['stop', 'exit', 'quit'].includes(command)"), 'stdin stop command should stop without sending an extra off packet');
assert(src.includes('safeTurnOff(device, leds'), 'live/effect shutdown should not crash if HID disappears while Windows is shutting down');
assert(src.includes('capture/write stopped cleanly after error'), 'live write errors should be logged and stopped cleanly instead of surfacing a node stack during shutdown');
