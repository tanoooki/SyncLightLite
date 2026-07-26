const HID = require('C:/tmp/synclight_asar/extract/node_modules/node-hid');

const VID = 0x1a86; // 6790
const PID = 0xfe07; // 65031
let nextId = 1;
function id() { nextId++; if (nextId >= 255) nextId = 1; return nextId; }
function checksum(buf) { let s = 0; for (const b of buf) s = (s + b) & 0xff; return s; }
function withReportId(buf) { return [0, ...buf]; }
function rbPacket(payloadLen, action) {
  const len = payloadLen;
  const b = Buffer.alloc(len);
  b.write('RB', 0, 2, 'utf8');
  b.writeUInt8(len, 2);
  b.writeUInt8(id(), 3);
  b.writeUInt8(action, 4);
  return b;
}
function getDeviceInfoPacket() {
  const b = rbPacket(6, 130);
  b.writeUInt8(checksum(b), 5);
  return b;
}
function setOpenUrlPacket(value = 0) {
  const b = rbPacket(7, 147);
  b.writeUInt8(value, 5);
  b.writeUInt8(checksum(b), 6);
  return b;
}
function setSectionPacket(section) {
  const len = 6 + section.length;
  const p = Buffer.alloc(len);
  p.write('RB', 0, 2, 'utf8');
  p.writeUInt8(len, 2);
  p.writeUInt8(id(), 3);
  p.writeUInt8(134, 4); // setSectionLED
  for (let i = 0; i < section.length; i++) p.writeUInt8(section[i], 5 + i);
  p.writeUInt8(checksum(p), len - 1);
  return p;
}
function setSolidColorPacket(r, g, b, lampsAmount = 71) {
  // Robobloq HID protocol module 60895: groups are [startLed, r, g, b, endLed].
  // SyncLight uses [1,r,g,b,254] or, when lamp count is known, [1,r,g,b,count,count+1,0,0,0,254].
  const section = (lampsAmount > 0 && lampsAmount < 254)
    ? [1, r, g, b, lampsAmount, lampsAmount + 1, 0, 0, 0, 254]
    : [1, r, g, b, 254];
  return setSectionPacket(section);
}
function thirdsData(lampsAmount = 71) {
  const a = Math.floor(lampsAmount / 3);
  const b = Math.floor((lampsAmount * 2) / 3);
  return [
    1, 255, 0, 0, a,
    a + 1, 0, 255, 0, b,
    b + 1, 0, 0, 255, lampsAmount,
    lampsAmount + 1, 0, 0, 0, 254,
  ];
}
function setThirdsPacket(lampsAmount = 71) {
  return setSectionPacket(thirdsData(lampsAmount));
}
function setSyncScreenPacket(data) {
  // HID sync protocol module 60895: SC + uint16 length + id + action=128 + 5-byte groups + checksum.
  const len = 7 + data.length;
  const p = Buffer.alloc(len);
  p.writeUInt8(83, 0); // S
  p.writeUInt8(67, 1); // C
  p.writeUInt16BE(len, 2);
  p.writeUInt8(id(), 4);
  p.writeUInt8(128, 5); // setSyncScreen
  for (let i = 0; i < data.length; i++) p.writeUInt8(data[i], 6 + i);
  p.writeUInt8(checksum(p), len - 1);
  return p;
}
function setSyncThirdsPacket(lampsAmount = 71) {
  return setSyncScreenPacket(thirdsData(lampsAmount));
}
function syncRangeData(start, end, r, g, b, lampsAmount = 71) {
  start = Math.max(1, Math.min(lampsAmount, start));
  end = Math.max(start, Math.min(lampsAmount, end));
  const data = [];
  if (start > 1) data.push(1, 0, 0, 0, start - 1);
  data.push(start, r, g, b, end);
  if (end < lampsAmount) data.push(end + 1, 0, 0, 0, lampsAmount);
  data.push(lampsAmount + 1, 0, 0, 0, 254);
  return data;
}
function setSyncRangePacket(start, end, r, g, b, lampsAmount = 71) {
  return setSyncScreenPacket(syncRangeData(start, end, r, g, b, lampsAmount));
}
function parseDeviceInfo(resp) {
  if (!resp || resp.length < 24) return null;
  // Response layout observed from this device and SyncLight's HID class (module 51545):
  // id = bytes 5..7, displaySize=8, lamps=11, uuid=12..19, version=21..23.
  return {
    rawHex: Buffer.from(resp).toString('hex'),
    header: Buffer.from(resp).slice(0, 2).toString('utf8'),
    requestId: resp[3],
    action: resp[4],
    id: Buffer.from(resp).slice(5, 8).toString('hex'),
    displaySize: resp[8],
    lampsAmount: resp[11],
    uuid: Buffer.from(resp).slice(12, 20).toString('hex'),
    version: `${resp[21]}.${resp[22]}.${resp[23]}`,
  };
}
function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }
async function main() {
  const cmd = process.argv[2] || 'info';
  const colorName = process.argv[3] || 'red';
  const colors = { red:[255,0,0], green:[0,255,0], blue:[0,0,255], white:[255,255,255], off:[0,0,0], magenta:[255,0,255], cyan:[0,255,255], yellow:[255,255,0] };
  const devs = HID.devices().filter(d => d.vendorId === VID && d.productId === PID && d.interface === 0);
  console.log('devices', JSON.stringify(devs, null, 2));
  if (!devs.length) throw new Error('Dispositivo HID da fita não encontrado');
  const dev = new HID.HID(devs[0].path);
  console.log('opened', devs[0].path);
  const infoPkt = getDeviceInfoPacket();
  console.log('write info', infoPkt.toString('hex'));
  dev.write(withReportId(infoPkt));
  const resp = dev.readTimeout(500);
  console.log('info response', resp && Buffer.from(resp).toString('hex'));
  const info = parseDeviceInfo(Buffer.from(resp || []));
  console.log('parsed', JSON.stringify(info, null, 2));
  const lampsAmount = info && info.lampsAmount || 71;
  if (cmd === 'color') {
    if (!colors[colorName]) throw new Error('Cor desconhecida: ' + colorName);
    const [r,g,b] = colors[colorName];
    console.log(`sending solid ${colorName}: rgb(${r},${g},${b}) lamps=${lampsAmount}`);
    // keep device out of URL/open helper state, like SyncLight does after info reads
    const openPkt = setOpenUrlPacket(0);
    dev.write(withReportId(openPkt));
    await sleep(80);
    const pkt = setSolidColorPacket(r, g, b, lampsAmount);
    console.log('write color', pkt.toString('hex'));
    dev.write(withReportId(pkt));
    await sleep(100);
  } else if (cmd === 'thirds') {
    console.log(`sending thirds map via setSectionLED: LEDs 1-${Math.floor(lampsAmount/3)} red, ${Math.floor(lampsAmount/3)+1}-${Math.floor(lampsAmount*2/3)} green, ${Math.floor(lampsAmount*2/3)+1}-${lampsAmount} blue`);
    const openPkt = setOpenUrlPacket(0);
    dev.write(withReportId(openPkt));
    await sleep(80);
    const pkt = setThirdsPacket(lampsAmount);
    console.log('write thirds', pkt.toString('hex'));
    dev.write(withReportId(pkt));
    await sleep(100);
  } else if (cmd === 'sync-thirds') {
    console.log(`sending thirds map via setSyncScreen: LEDs 1-${Math.floor(lampsAmount/3)} red, ${Math.floor(lampsAmount/3)+1}-${Math.floor(lampsAmount*2/3)} green, ${Math.floor(lampsAmount*2/3)+1}-${lampsAmount} blue`);
    const openPkt = setOpenUrlPacket(0);
    dev.write(withReportId(openPkt));
    await sleep(80);
    const pkt = setSyncThirdsPacket(lampsAmount);
    console.log('write sync-thirds', pkt.toString('hex'));
    dev.write(withReportId(pkt));
    await sleep(100);
  } else if (cmd === 'sync-range') {
    const start = parseInt(process.argv[3] || '1', 10);
    const end = parseInt(process.argv[4] || String(start), 10);
    const color = process.argv[5] || 'white';
    if (!colors[color]) throw new Error('Cor desconhecida: ' + color);
    const [r,g,b] = colors[color];
    console.log(`sending sync range: LEDs ${start}-${end} ${color}, rest off`);
    const openPkt = setOpenUrlPacket(0);
    dev.write(withReportId(openPkt));
    await sleep(80);
    const pkt = setSyncRangePacket(start, end, r, g, b, lampsAmount);
    console.log('write sync-range', pkt.toString('hex'));
    dev.write(withReportId(pkt));
    await sleep(100);
  } else {
    console.log('info only; no color command sent');
  }
  dev.close();
}
main().catch(e => { console.error(e.stack || e); process.exit(1); });
