let nextId = 1;

function id() {
  nextId += 1;
  if (nextId >= 255) nextId = 1;
  return nextId;
}

function checksum(buf) {
  let sum = 0;
  for (const byte of buf) sum = (sum + byte) & 0xff;
  return sum;
}

function rbPacket(length, action) {
  const packet = Buffer.alloc(length);
  packet.write('RB', 0, 2, 'utf8');
  packet.writeUInt8(length, 2);
  packet.writeUInt8(id(), 3);
  packet.writeUInt8(action, 4);
  return packet;
}

function infoPacket() {
  const packet = rbPacket(6, 130);
  packet.writeUInt8(checksum(packet), 5);
  return packet;
}

function openUrlPacket(value = 0) {
  const packet = rbPacket(7, 147);
  packet.writeUInt8(value, 5);
  packet.writeUInt8(checksum(packet), 6);
  return packet;
}

function sectionPacket(data) {
  const length = 6 + data.length;
  const packet = rbPacket(length, 134);
  for (let i = 0; i < data.length; i++) packet.writeUInt8(data[i], 5 + i);
  packet.writeUInt8(checksum(packet), length - 1);
  return packet;
}

function solidSectionData(r, g, b, leds = 71) {
  return leds > 0 && leds < 254
    ? [1, r, g, b, leds, leds + 1, 0, 0, 0, 254]
    : [1, r, g, b, 254];
}

function solidPacket(r, g, b, leds = 71) {
  return sectionPacket(solidSectionData(r, g, b, leds));
}

function syncScreenPacket(data) {
  const length = 7 + data.length;
  const packet = Buffer.alloc(length);
  packet.writeUInt8(83, 0); // S
  packet.writeUInt8(67, 1); // C
  packet.writeUInt16BE(length, 2);
  packet.writeUInt8(id(), 4);
  packet.writeUInt8(128, 5);
  for (let i = 0; i < data.length; i++) packet.writeUInt8(data[i], 6 + i);
  packet.writeUInt8(checksum(packet), length - 1);
  return packet;
}

function rangeData(start, end, r, g, b, leds = 71) {
  start = Math.max(1, Math.min(leds, start));
  end = Math.max(start, Math.min(leds, end));
  const data = [];
  if (start > 1) data.push(1, 0, 0, 0, start - 1);
  data.push(start, r, g, b, end);
  if (end < leds) data.push(end + 1, 0, 0, 0, leds);
  data.push(leds + 1, 0, 0, 0, 254);
  return data;
}

function colorsToSyncData(colors) {
  const data = [];
  for (let i = 0; i < colors.length; i++) {
    const c = colors[i];
    data.push(i + 1, clamp(c.r), clamp(c.g), clamp(c.b), i + 1);
  }
  data.push(colors.length + 1, 0, 0, 0, 254);
  return data;
}

function colorsPacket(colors) {
  return syncScreenPacket(colorsToSyncData(colors));
}

function parseInfo(response) {
  if (!response || response.length < 24) return null;
  return {
    rawHex: Buffer.from(response).toString('hex'),
    header: Buffer.from(response).slice(0, 2).toString('utf8'),
    requestId: response[3],
    action: response[4],
    id: Buffer.from(response).slice(5, 8).toString('hex'),
    displaySize: response[8],
    lampsAmount: response[11],
    uuid: Buffer.from(response).slice(12, 20).toString('hex'),
    version: `${response[21]}.${response[22]}.${response[23]}`,
  };
}

function clamp(value) {
  return Math.max(0, Math.min(255, Math.round(value || 0)));
}

module.exports = {
  checksum,
  infoPacket,
  openUrlPacket,
  sectionPacket,
  solidPacket,
  syncScreenPacket,
  rangeData,
  colorsToSyncData,
  colorsPacket,
  parseInfo,
  clamp,
};
