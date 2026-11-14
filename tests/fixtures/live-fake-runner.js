// Runs the real engine in a VM with native/HID dependencies replaced. Never opens hardware.
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const root = path.resolve(__dirname, '../..');
const scenario = process.argv[2];
const mode = process.argv[3] || 'screen';
let closed = false, frameTimer, callback;
const device = { close() { closed = true; console.log('FAKE close'); } };
const frames = { 1: Buffer.alloc(100 * 100 * 4, 200) };
const q = {
  getMonitors: () => [{ displayId: 1, width: 1000, height: 1000, isPrimary: true }],
  startCaptureMultiScreens(cb) {
    callback = cb;
    frameTimer = setInterval(() => cb(frames), 40);
  },
  stopCaptureMultiScreens() {
    console.log('FAKE capture-stop');
    clearInterval(frameTimer);
    if (scenario === 'blocking-stop') Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 300);
    // Native callback already queued before cancellation.
    setTimeout(() => callback(frames), 10);
  },
};

const hid = {
  openLight: () => ({ device, info: {} }),
  readInfo: () => ({ lampsAmount: 71 }),
  writePacket(dev, packet) {
    if (closed) { console.log('FAKE write-after-close'); throw Error('Cannot write to closed device'); }
    const isOff = packet[4] === 134 && packet[6] === 0 && packet[7] === 0 && packet[8] === 0;
    if (isOff && scenario === 'off-fails') throw Error('Cannot write to hid device');
    if (packet[4] === 134 && !isOff && scenario === 'effect-error') throw Error('fake effect HID failure');
    if (Buffer.from(packet).subarray(0, 2).toString() === 'SC' && scenario === 'capture-error') throw Error('fake capture HID failure');
    console.log(isOff ? 'FAKE off-write' : 'FAKE packet');
  },
};
function safeRequire(name) {
  if (name === 'fake-quiklight') return q;
  if (name === './hid') return hid;
  if (name === 'fs') return { readFileSync: () => JSON.stringify({ mode, fps: 30 }) };
  if (name === 'path') return path;
  if (name === './packets' || name === './mapper') return require(path.join(root, 'src', name));
  throw Error('Blocked unexpected dependency: ' + name);
}
safeRequire.resolve = name => {
  if (name === '@warren-robobloq/quiklight') return 'fake-quiklight';
  throw Error('Blocked resolve: ' + name);
};
process.argv = [process.execPath, path.join(root, 'src/live.js'), 'live'];
vm.runInNewContext(fs.readFileSync(path.join(root, 'src/live.js'), 'utf8'), {
  require: safeRequire, __dirname: path.join(root, 'src'), process, console, Buffer,
  setTimeout, clearTimeout, setInterval, clearInterval,
}, { filename: 'live.js' });
