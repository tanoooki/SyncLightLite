function frameToLedColors(frame, width, height, ledCount = 71, options = {}) {
  const captureScale = clamp(Number(options.captureScale ?? 1), 0.35, 1);
  const headerOffset = clamp(Number(options.headerOffset ?? 0), 0, 0.65);
  const horizontalMargin = Math.round(width * clamp(Number(options.horizontalMargin ?? 0), 0, 0.25));
  const topCount = Math.round(ledCount * width / (width + 2 * height));
  const sideCount = Math.floor((ledCount - topCount) / 2);
  const extra = ledCount - topCount - 2 * sideCount;
  const rightCount = sideCount + extra;
  const leftCount = sideCount;

  const sampleY = (t) => {
    const offset = headerOffset;
    const scale = Math.min(captureScale, 1 - offset);
    return Math.round((height - 1) * (offset + t * scale));
  };
  const sampleX = (t) => Math.round(horizontalMargin + ((width - 1) - horizontalMargin * 2) * t);

  const colors = [];

  // Physical/logical order observed: LED 1 starts at the USB cable side.
  // Viewed from the front, sync-thirds appeared blue/green/red left-to-right,
  // so logical order maps roughly: right side -> top (right-to-left) -> left side.
  for (let i = 0; i < rightCount; i++) {
    const t = rightCount <= 1 ? 0 : i / (rightCount - 1);
    colors.push(sampleRegion(frame, width, height, width - 6 - horizontalMargin, sampleY(1 - t), 6, 8));
  }
  for (let i = 0; i < topCount; i++) {
    const t = topCount <= 1 ? 0 : i / (topCount - 1);
    colors.push(sampleRegion(frame, width, height, sampleX(1 - t), sampleY(0), 10, 6));
  }
  for (let i = 0; i < leftCount; i++) {
    const t = leftCount <= 1 ? 0 : i / (leftCount - 1);
    colors.push(sampleRegion(frame, width, height, horizontalMargin, sampleY(t), 6, 8));
  }

  const mapped = colors.slice(0, ledCount);
  return options.reverseLedOrder ? mapped.reverse() : mapped;
}

function sampleRegion(frame, width, height, cx, cy, rw, rh) {
  const x0 = Math.max(0, Math.floor(cx - rw / 2));
  const x1 = Math.min(width - 1, Math.ceil(cx + rw / 2));
  const y0 = Math.max(0, Math.floor(cy - rh / 2));
  const y1 = Math.min(height - 1, Math.ceil(cy + rh / 2));
  let r = 0, g = 0, b = 0, n = 0;
  for (let y = y0; y <= y1; y++) {
    for (let x = x0; x <= x1; x++) {
      const idx = (y * width + x) * 3;
      r += frame[idx];
      g += frame[idx + 1];
      b += frame[idx + 2];
      n++;
    }
  }
  if (!n) return { r: 0, g: 0, b: 0 };
  return boost({ r: r / n, g: g / n, b: b / n });
}

function boost(color) {
  // Mild brightness/saturation help LEDs read as screen edge colors without overdriving USB power.
  const max = Math.max(color.r, color.g, color.b, 1);
  const scale = Math.min(1.25, 255 / max);
  return { r: color.r * scale, g: color.g * scale, b: color.b * scale };
}

function processColors(colors, options = {}) {
  const brightness = clamp(Number(options.brightness ?? 1), 0, 1.5);
  const saturation = clamp(Number(options.saturation ?? 1), 0, 2.5);
  const gamma = clamp(Number(options.gamma ?? 1), 0.4, 3);
  const blackFloor = clamp(Number(options.blackFloor ?? 0), 0, 0.25) * 255;
  return colors.map(c => {
    let r = c.r, g = c.g, b = c.b;
    const max = Math.max(r, g, b);
    if (max < blackFloor) return { r: 0, g: 0, b: 0 };
    const gray = (r + g + b) / 3;
    r = gray + (r - gray) * saturation;
    g = gray + (g - gray) * saturation;
    b = gray + (b - gray) * saturation;
    r = Math.pow(clamp(r / 255, 0, 1), gamma) * 255 * brightness;
    g = Math.pow(clamp(g / 255, 0, 1), gamma) * 255 * brightness;
    b = Math.pow(clamp(b / 255, 0, 1), gamma) * 255 * brightness;
    return { r: clamp(r, 0, 255), g: clamp(g, 0, 255), b: clamp(b, 0, 255) };
  });
}

function colorDistance(a, b) {
  if (!a || !b || a.length !== b.length) return Infinity;
  let total = 0;
  for (let i = 0; i < a.length; i++) {
    total += Math.abs(a[i].r - b[i].r) + Math.abs(a[i].g - b[i].g) + Math.abs(a[i].b - b[i].b);
  }
  return total / (a.length * 3);
}

function smoothColors(previous, next, alpha = 0.35) {
  if (!previous || previous.length !== next.length) return next;
  return next.map((c, i) => ({
    r: previous[i].r * (1 - alpha) + c.r * alpha,
    g: previous[i].g * (1 - alpha) + c.g * alpha,
    b: previous[i].b * (1 - alpha) + c.b * alpha,
  }));
}

function clamp(value, min, max) {
  if (!Number.isFinite(value)) return min;
  return Math.max(min, Math.min(max, value));
}

module.exports = { frameToLedColors, sampleRegion, processColors, colorDistance, smoothColors };
