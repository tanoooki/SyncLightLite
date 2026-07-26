const assert = require('assert');
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

const colors = frameToLedColors(makeFrame(256, 108), 256, 108, 71);
const first = colors.slice(0, 8);
const middle = colors.slice(30, 40);
const last = colors.slice(-8);
assert(first.every(c => c.r > c.g && c.r > c.b), 'logical LED start should sample right edge red');
assert(middle.some(c => c.g > c.r && c.g > c.b), 'middle LEDs should include top edge green');
assert(last.every(c => c.b > c.r && c.b > c.g), 'logical LED end should sample left edge blue');
