const path = require('path');

function moduleRoot() {
  const roots = [
    process.env.SYNCLIGHTLITE_NODE_MODULES,
    path.join(__dirname, '..', 'vendor', 'node_modules'),
  ].filter(Boolean);
  for (const root of roots) {
    try { return require.resolve('node-hid', { paths: [root] }).replace(/node-hid.*/, ''); } catch (_) {}
  }
  throw new Error('node-hid dependency not found; reinstall SyncLightLite');
}

const HID = require(require.resolve('node-hid', { paths: [moduleRoot()] }));

const VID = 0x1a86;
const PID = 0xfe07;

function findLight() {
  return HID.devices().find(d => d.vendorId === VID && d.productId === PID && d.interface === 0);
}

function openLight() {
  const info = findLight();
  if (!info) throw new Error('Robobloq SyncLight HID device not found');
  const device = new HID.HID(info.path);
  return { info, device };
}

function packetChunks(packet, size = 64) {
  const chunks = [];
  for (let i = 0; i < packet.length; i += size) chunks.push(packet.slice(i, i + size));
  return chunks;
}

function writePacket(device, packet) {
  for (const chunk of packetChunks(packet)) device.write([0, ...chunk]);
}

function readInfo(device, makeInfoPacket, parseInfo) {
  const packet = makeInfoPacket();
  writePacket(device, packet);
  const response = Buffer.from(device.readTimeout(500) || []);
  return parseInfo(response);
}

module.exports = { HID, VID, PID, findLight, openLight, packetChunks, writePacket, readInfo, moduleRoot };
