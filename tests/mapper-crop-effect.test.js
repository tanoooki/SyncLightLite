const assert = require('assert');
const path = require('path');
const root = path.resolve(__dirname, '..');
const { frameToLedColors } = require(path.join(root, 'src/mapper.js'));

function makeFrame(width, height) {
  const frame = Buffer.alloc(width * height * 3);
  for (let y = 0; y < height; y++) {
    const green = Math.round(255 * y / (height - 1));
    for (let x = 0; x < width; x++) {
      const idx = (y * width + x) * 3;
      frame[idx + 1] = green;
    }
  }
  return frame;
}

const frame = makeFrame(100, 100);
const full = frameToLedColors(frame, 100, 100, 71, { captureScale: 1, headerOffset: 0 });
const cropped = frameToLedColors(frame, 100, 100, 71, { captureScale: 1, headerOffset: 0.5 });
const fullTop = full[Math.floor(full.length / 2)].g;
const croppedTop = cropped[Math.floor(cropped.length / 2)].g;
assert(croppedTop > fullTop + 80, 'headerOffset=0.5 should move top sampling toward the middle/bottom of the frame');
