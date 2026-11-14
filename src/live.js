const fs = require('fs');
const path = require('path');
function moduleRoot() {
  const roots = [
    process.env.SYNCLIGHTLITE_NODE_MODULES,
    path.join(__dirname, '..', 'vendor', 'node_modules'),
  ].filter(Boolean);
  for (const root of roots) {
    try { require.resolve('@warren-robobloq/quiklight', { paths: [root] }); return root; } catch (_) {}
  }
  throw new Error('quiklight dependency not found; reinstall SyncLightLite');
}
const q = require(require.resolve('@warren-robobloq/quiklight', { paths: [moduleRoot()] }));
const { openLight, writePacket, readInfo } = require('./hid');
const { infoPacket, parseInfo, openUrlPacket, solidPacket, colorsPacket, rangeData, syncScreenPacket } = require('./packets');
const { frameToLedColors, processColors, colorDistance, smoothColors } = require('./mapper');

const COLORS = {
  red: [255, 0, 0],
  green: [0, 255, 0],
  blue: [0, 0, 255],
  white: [255, 255, 255],
  off: [0, 0, 0],
};

function argValue(name, fallback) {
  const idx = process.argv.indexOf(name);
  return idx >= 0 && process.argv[idx + 1] ? process.argv[idx + 1] : fallback;
}

function argBool(name, fallback) {
  const idx = process.argv.indexOf(name);
  if (idx < 0) return Boolean(fallback);
  const next = process.argv[idx + 1];
  if (!next || next.startsWith('--')) return true;
  return ['1', 'true', 'yes', 'on'].includes(String(next).toLowerCase());
}

function openAndRead() {
  const light = openLight();
  const deviceInfo = readInfo(light.device, infoPacket, parseInfo);
  return { ...light, deviceInfo };
}

async function main() {
  const cmd = process.argv[2] || 'info';
  const light = openAndRead();
  const leds = light.deviceInfo?.lampsAmount || 71;
  console.log('device', JSON.stringify({ hid: light.info, device: light.deviceInfo }, null, 2));

  if (cmd === 'info') {
    light.device.close();
    return;
  }

  writePacket(light.device, openUrlPacket(0));
  await sleep(80);

  if (cmd === 'solid') {
    const color = process.argv[3] || 'white';
    const rgb = COLORS[color];
    if (!rgb) throw new Error(`unknown color: ${color}`);
    writePacket(light.device, solidPacket(rgb[0], rgb[1], rgb[2], leds));
    console.log(`sent solid ${color}`);
    light.device.close();
    return;
  }

  if (cmd === 'off') {
    turnOff(light.device, leds);
    console.log('sent off');
    light.device.close();
    return;
  }

  if (cmd === 'range') {
    const start = Number(process.argv[3] || '1');
    const end = Number(process.argv[4] || String(start));
    const color = process.argv[5] || 'white';
    const rgb = COLORS[color];
    if (!rgb) throw new Error(`unknown color: ${color}`);
    writePacket(light.device, syncScreenPacket(rangeData(start, end, rgb[0], rgb[1], rgb[2], leds)));
    console.log(`sent range ${start}-${end} ${color}; rest off`);
    light.device.close();
    return;
  }

  if (cmd === 'live') {
    await runLive(light.device, leds);
    return;
  }

  throw new Error(`unknown command: ${cmd}`);
}

async function runLive(device, leds) {
  const config = loadConfig();
  const mode = normalizeMode(config.mode);
  if (mode !== 'screen') return runEffect(device, leds, config, mode);
  const monitors = q.getMonitors();
  const monitor = monitors.find(m => m.isPrimary) || monitors[0];
  if (!monitor) throw new Error('no monitor found');

  const duration = Number(argValue('--duration', '0')) * 1000;
  const fps = Number(argValue('--fps', String(config.fps ?? 30)));
  const captureScale = Number(argValue('--capture-scale', String((config.captureScalePercent ?? 100) / 100)));
  const headerOffset = Number(argValue('--header-offset', String((config.headerOffsetPercent ?? 0) / 100)));
  const smoothing = Number(argValue('--smoothing', String(config.smoothing ?? 0.35)));
  const brightness = Number(argValue('--brightness', String((config.brightnessPercent ?? 100) / 100)));
  const saturation = Number(argValue('--saturation', String((config.saturationPercent ?? 100) / 100)));
  const gamma = Number(argValue('--gamma', String(config.gamma ?? 1)));
  const threshold = Number(argValue('--threshold', String((config.thresholdPercent ?? 0) * 2.55)));
  const horizontalMargin = Number(argValue('--horizontal-margin', String((config.horizontalMarginPercent ?? 0) / 100)));
  const blackFloor = Number(argValue('--black-floor', String((config.blackFloorPercent ?? 0) / 100)));
  const reverseLedOrder = argBool('--reverse-led-order', config.reverseLedOrder ?? false);
  const intervalMs = Math.max(8, Math.round(1000 / fps));
  const started = Date.now();
  let lastSent = 0;
  let smoothed = null;
  let lastOutput = null;
  let sent = 0;
  let lastHeartbeat = 0;
  let stopped = false;

  console.log('live', JSON.stringify({ mode, monitor, leds, durationMs: duration, fps, captureScale, headerOffset, horizontalMargin, brightness, saturation, gamma, threshold, blackFloor, smoothing, reverseLedOrder }));

  return new Promise((resolve) => {
    const stop = (turnLightsOff = false) => {
      if (stopped) return;
      stopped = true;
      const offWritten = turnLightsOff && safeTurnOff(device, leds, 'live stop');
      console.log('SYNCLIGHT_STOP ' + JSON.stringify({ offRequested: turnLightsOff, offWritten }));
      try { q.stopCaptureMultiScreens(); } catch (_) {}
      try { device.close(); } catch (_) {}
      console.log(`live stopped; packets sent=${sent}; off=${offWritten}`);
      resolve();
    };

    const stopAfterDeviceError = (err) => {
      if (timer) clearTimeout(timer);
      console.log(`[live] capture/write stopped cleanly after error: ${err?.message || err}`);
      stop(false);
      // stdin/native handles can keep an otherwise stopped engine alive.
      setTimeout(() => process.exit(1), 50);
    };

    installStdinShutdownHandler(stop);
    installShutdownHandlers(() => stop(true));
    const timer = duration > 0 ? setTimeout(() => stop(false), duration) : null;

    try {
      q.startCaptureMultiScreens((frames) => {
        if (stopped) return; // Native callbacks may already be queued when capture stops.
        try {
          if (Date.now() - lastSent < intervalMs) return;
          const frame = frames[monitor.displayId];
          if (!frame) return;
          const width = Math.round(monitor.width / 10);
          const height = Math.round(monitor.height / 10);
          let colors = frameToLedColors(frame, width, height, leds, { captureScale, headerOffset, horizontalMargin, reverseLedOrder });
          colors = processColors(colors, { brightness, saturation, gamma, blackFloor });
          smoothed = smoothColors(smoothed, colors, smoothing);
          if (threshold > 0 && lastOutput && colorDistance(lastOutput, smoothed) < threshold) return;
          writePacket(device, colorsPacket(smoothed));
          lastOutput = smoothed;
          lastSent = Date.now();
          sent++;
          if (Date.now() - lastHeartbeat >= 5000) {
            lastHeartbeat = Date.now();
            console.log(`live heartbeat; packets sent=${sent}`);
          }
          if (duration > 0 && Date.now() - started >= duration) {
            if (timer) clearTimeout(timer);
            stop(false);
          }
        } catch (err) {
          stopAfterDeviceError(err);
        }
      }, monitors, 120, 10);
    } catch (err) {
      stopAfterDeviceError(err);
    }
  });
}

async function runEffect(device, leds, config, mode) {
  const fps = Number(argValue('--fps', String(config.fps ?? 30)));
  const speed = Math.max(1, Number(config.effectSpeedPercent ?? 50)) / 50;
  const base = parseHexColor(config.effectColor ?? '#ffffff');
  const intervalMs = Math.max(16, Math.round(1000 / Math.max(1, Math.min(60, fps))));
  let tick = 0;
  let sent = 0;
  let stopped = false;
  console.log('effect', JSON.stringify({ mode, leds, fps, speed, effectColor: config.effectColor ?? '#ffffff' }));

  return new Promise((resolve) => {
    let timer = null;
    const stop = (turnLightsOff = false) => {
      if (stopped) return;
      stopped = true;
      if (timer) clearInterval(timer);
      const offWritten = turnLightsOff && safeTurnOff(device, leds, 'effect stop');
      console.log('SYNCLIGHT_STOP ' + JSON.stringify({ offRequested: turnLightsOff, offWritten }));
      try { device.close(); } catch (_) {}
      console.log(`effect stopped; mode=${mode}; packets sent=${sent}; off=${offWritten}`);
      resolve();
    };
    installStdinShutdownHandler(stop);
    installShutdownHandlers(() => stop(true));
    timer = setInterval(() => {
      try {
        tick++;
        const colors = effectColors(mode, leds, tick, speed, base);
        if (mode === 'solid') writePacket(device, solidPacket(base.r, base.g, base.b, leds));
        else writePacket(device, colorsPacket(colors));
        sent++;
        if (sent === 1 || sent % Math.max(1, Math.round(5000 / intervalMs)) === 0) console.log(`effect heartbeat; mode=${mode}; packets sent=${sent}`);
      } catch (err) {
        console.log(`[live] effect/write stopped cleanly after error: ${err?.message || err}`);
        stop(false);
        setTimeout(() => process.exit(1), 50);
      }
    }, intervalMs);
  });
}

function normalizeMode(mode) {
  const value = String(mode || 'screen');
  return ['screen', 'solid', 'breathing', 'rainbow', 'colorCycle', 'wave'].includes(value) ? value : 'screen';
}

function parseHexColor(hex) {
  const m = String(hex || '#ffffff').match(/^#?([0-9a-f]{6})$/i);
  const value = m ? m[1] : 'ffffff';
  return { r: parseInt(value.slice(0, 2), 16), g: parseInt(value.slice(2, 4), 16), b: parseInt(value.slice(4, 6), 16) };
}

function effectColors(mode, leds, tick, speed, base) {
  const colors = [];
  const phase = tick * 0.025 * speed;
  for (let i = 0; i < leds; i++) {
    if (mode === 'breathing') {
      const f = (Math.sin(phase * Math.PI * 2) + 1) / 2;
      colors.push({ r: base.r * f, g: base.g * f, b: base.b * f });
    } else if (mode === 'rainbow') {
      colors.push(hsvToRgb(((i / leds) + phase) % 1, 1, 1));
    } else if (mode === 'colorCycle') {
      colors.push(hsvToRgb(phase % 1, 1, 1));
    } else if (mode === 'wave') {
      const f = (Math.sin((i / leds) * Math.PI * 4 + phase * Math.PI * 2) + 1) / 2;
      colors.push({ r: base.r * f, g: base.g * f, b: base.b * f });
    } else {
      colors.push({ ...base });
    }
  }
  return colors;
}

function hsvToRgb(h, s, v) {
  const i = Math.floor(h * 6);
  const f = h * 6 - i;
  const p = v * (1 - s);
  const q = v * (1 - f * s);
  const t = v * (1 - (1 - f) * s);
  const [r, g, b] = [[v, t, p], [q, v, p], [p, v, t], [p, q, v], [t, p, v], [v, p, q]][i % 6];
  return { r: r * 255, g: g * 255, b: b * 255 };
}

function installShutdownHandlers(stop) {
  process.on('SIGINT', () => { stop(); setTimeout(() => process.exit(0), 50); });
  process.on('SIGTERM', () => { stop(); setTimeout(() => process.exit(0), 50); });
  process.on('SIGHUP', () => { stop(); setTimeout(() => process.exit(0), 50); });
}

function installStdinShutdownHandler(stop) {
  if (!process.stdin || process.stdin.isTTY) return;
  process.stdin.setEncoding('utf8');
  let pending = '';
  process.stdin.on('data', chunk => {
    pending += String(chunk);
    let newline;
    while ((newline = pending.indexOf('\n')) >= 0) {
      const command = pending.slice(0, newline).trim().toLowerCase();
      pending = pending.slice(newline + 1);
      if (['off', 'shutdown', 'stop-off'].includes(command)) {
        stop(true);
        setTimeout(() => process.exit(0), 50);
        return;
      }
      if (['stop', 'exit', 'quit'].includes(command)) {
        stop(false);
        setTimeout(() => process.exit(0), 50);
      }
    }
  });
}

function safeTurnOff(device, leds, context) {
  try {
    turnOff(device, leds);
    return true;
  } catch (err) {
    console.log(`[live] lights-off skipped during ${context}: ${err?.message || err}`);
    return false;
  }
}

function turnOff(device, leds) {
  writePacket(device, solidPacket(0, 0, 0, leds));
}

function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

function loadConfig() {
  const configPath = path.join(__dirname, '..', 'config.json');
  const defaults = { mode: 'screen', effectColor: '#ffffff', effectSpeedPercent: 50, fps: 30, captureScalePercent: 100, headerOffsetPercent: 0, smoothing: 0.35, brightnessPercent: 100, saturationPercent: 100, gamma: 1, thresholdPercent: 0, horizontalMarginPercent: 0, blackFloorPercent: 0, reverseLedOrder: false };
  try {
    return normalizeConfig(JSON.parse(fs.readFileSync(configPath, 'utf8')));
  } catch (_) {
    return defaults;
  }
}

function normalizeConfig(raw) {
  return {
    mode: normalizeMode(raw.mode ?? raw.Mode ?? 'screen'),
    effectColor: String(raw.effectColor ?? raw.EffectColor ?? '#ffffff'),
    effectSpeedPercent: Number(raw.effectSpeedPercent ?? raw.EffectSpeedPercent ?? 50),
    fps: Number(raw.fps ?? raw.Fps ?? 30),
    captureScalePercent: Number(raw.captureScalePercent ?? raw.CaptureScalePercent ?? 100),
    headerOffsetPercent: Number(raw.headerOffsetPercent ?? raw.HeaderOffsetPercent ?? 0),
    smoothing: Number(raw.smoothing ?? raw.Smoothing ?? 0.35),
    brightnessPercent: Number(raw.brightnessPercent ?? raw.BrightnessPercent ?? 100),
    saturationPercent: Number(raw.saturationPercent ?? raw.SaturationPercent ?? 100),
    gamma: Number(raw.gamma ?? raw.Gamma ?? 1),
    thresholdPercent: Number(raw.thresholdPercent ?? raw.ThresholdPercent ?? 0),
    horizontalMarginPercent: Number(raw.horizontalMarginPercent ?? raw.HorizontalMarginPercent ?? 0),
    blackFloorPercent: Number(raw.blackFloorPercent ?? raw.BlackFloorPercent ?? 0),
    reverseLedOrder: Boolean(raw.reverseLedOrder ?? raw.ReverseLedOrder ?? false),
  };
}

main().catch(err => {
  console.error(err.stack || err);
  process.exit(1);
});
