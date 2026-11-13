const assert = require('assert');
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const { execFileSync } = require('child_process');
const root = path.resolve(__dirname, '..');
const project = path.join(__dirname, 'CaptureHarness/CaptureHarness.csproj');
execFileSync('dotnet', ['build', project, '--nologo', '-v:q'], { stdio: 'inherit' });
const fixtures = [];
for (const [width, height] of [[1920,1080],[2560,1440],[3440,1440],[1080,1920],[1366,768],[3840,2160]])
  for (const [scale, offset, margin] of [[1,0,0],[.9,.23,.1],[.35,.65,.25],[.35,0,.25],[1,.65,0],[.99,.01,.005],[-1,-1,-1],[2,2,2]])
    for (const reverse of [false,true]) fixtures.push({ width, height, scale, offset, margin, reverse });
const fixturePath = path.join(__dirname, 'CaptureHarness/obj/fixtures.jsonl');
fs.writeFileSync(fixturePath, fixtures.map(x => JSON.stringify(x)).join('\n'));
const output = execFileSync('dotnet', [path.join(__dirname, 'CaptureHarness/bin/Debug/net9.0-windows/CaptureHarness.dll'), fixturePath], { encoding: 'utf8' });
const actual = output.trim().split(/\r?\n/).map(JSON.parse);
assert.equal(actual.length, fixtures.length);
// Instrument the real mapper without duplicating its coordinate formulas.
const sandbox = { module: { exports: {} } };
vm.createContext(sandbox);
vm.runInContext(fs.readFileSync(path.join(root, 'src/mapper.js'), 'utf8') + `
var accessed = [];
var originalSample = sampleRegion;
sampleRegion = function(frame,w,h,cx,cy,rw,rh) {
  var pixels = [];
  var proxy = new Proxy({}, { get: (_, key) => { pixels.push(Math.floor(Number(key)/3)); return 0; } });
  originalSample(proxy,w,h,cx,cy,rw,rh);
  var xs = pixels.map(p => p % w), ys = pixels.map(p => Math.floor(p/w));
  accessed.push({ X: Math.min(...xs), Y: Math.min(...ys), Width: Math.max(...xs)-Math.min(...xs)+1, Height: Math.max(...ys)-Math.min(...ys)+1 });
  return {r:0,g:0,b:0};
};`, sandbox);
fixtures.forEach((f,i) => {
  const w = Math.round(f.width/10), h = Math.round(f.height/10);
  sandbox.accessed = [];
  sandbox.module.exports.frameToLedColors([],w,h,71,{captureScale:f.scale,headerOffset:f.offset,horizontalMargin:f.margin,reverseLedOrder:f.reverse});
  const expected = JSON.parse(JSON.stringify(sandbox.accessed));
  if(f.reverse) expected.reverse();
  assert.equal(actual[i].FrameWidth,w);
  assert.equal(actual[i].FrameHeight,h);
  assert.deepStrictEqual(actual[i].Samples.map(({X,Y,Width,Height}) => ({X,Y,Width,Height})),expected,JSON.stringify(f));
  assert(actual[i].Samples.every(s => ['Right','Top','Left'].includes(s.Edge)), 'no bottom LED strip');
});
console.log(`Capture geometry: ${fixtures.length} real JS/C# parity cases passed`);
