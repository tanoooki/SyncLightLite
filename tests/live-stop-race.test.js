const assert = require('assert');
const path = require('path');
const { spawn } = require('child_process');
function run(scenario, mode = 'screen', command = 'off\n') {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, [path.join(__dirname, 'fixtures/live-fake-runner.js'), scenario, mode], { windowsHide: true });
    let output = '', sent = false;
    const timeout = setTimeout(() => { child.kill(); reject(Error('engine remained alive: ' + output)); }, 1800);
    child.stdout.on('data', data => {
      output += data;
      if (!sent && output.includes(mode === 'screen' ? 'live {' : 'effect {')) {
        sent = true;
        if (Array.isArray(command)) { child.stdin.write(command[0]); setTimeout(() => child.stdin.write(command[1]), 30); }
        else if (command) child.stdin.write(command);
      }
    });
    child.stderr.on('data', data => { output += data; });
    child.on('error', reject);
    child.on('exit', code => { clearTimeout(timeout); resolve({ code, output }); });
  });
}
(async () => {
  const failed = await run('off-fails');
  assert(failed.output.includes('off=false'), 'failed HID off must not be logged as off=true: ' + failed.output);
  assert(failed.output.includes('SYNCLIGHT_STOP {"offRequested":true,"offWritten":false}'), 'stdin stop must acknowledge actual failure');
  console.log('PASS actual HID off failure acknowledgement');
  const late = await run('normal');
  assert(!late.output.includes('write-after-close'), 'queued capture callback must not touch a closed device: ' + late.output);
  assert(late.output.includes('SYNCLIGHT_STOP {"offRequested":true,"offWritten":true}'));
  console.log('PASS queued capture callback ignored after stop');
  const blocking = await run('blocking-stop');
  assert(blocking.output.indexOf('FAKE off-write') < blocking.output.indexOf('FAKE capture-stop'), 'HID off must precede potentially blocking native capture stop');
  assert(blocking.output.indexOf('SYNCLIGHT_STOP ') < blocking.output.indexOf('FAKE capture-stop'), 'off acknowledgement must precede native stop');
  console.log('PASS off and ACK before native capture stop');
  const captureError = await run('capture-error', 'screen', null);
  assert.strictEqual(captureError.code, 1, 'fatal capture/write errors must terminate nonzero even with stdin open');
  console.log('PASS fatal capture error exits with open stdin');
  for (const scenario of ['normal', 'off-fails']) {
    const effect = await run(scenario, 'solid');
    assert(effect.output.includes(`SYNCLIGHT_STOP {"offRequested":true,"offWritten":${scenario === 'normal'}}`), 'effect must acknowledge actual off result: ' + effect.output);
    assert(effect.output.includes('off=' + (scenario === 'normal')));
  }
  console.log('PASS effect off success/failure acknowledgement');
  const effectError = await run('effect-error', 'solid', null);
  assert.strictEqual(effectError.code, 1, 'effect write errors must terminate nonzero with stdin open');
  console.log('PASS fatal effect error exits with open stdin');
  const split = await run('normal', 'screen', ['o', 'ff\n']);
  assert(split.output.includes('"offWritten":true'), 'stdin pipe chunks are not command boundaries');
  console.log('PASS split stdin command acknowledged');
})().catch(err => { console.error(err); process.exitCode = 1; });
