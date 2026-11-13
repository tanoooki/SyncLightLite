const assert = require('assert');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');
const root = path.resolve(__dirname, '..');
const runner = path.join(root, 'scripts/run-tests.js');
assert(fs.existsSync(runner), 'A discovering test runner must support an explicit source-only mode');
const fixture = fs.mkdtempSync(path.join(process.env.TMPDIR || os.tmpdir(), 'synclight-tests-'));
try {
  fs.mkdirSync(path.join(fixture, 'scripts'));
  fs.mkdirSync(path.join(fixture, 'tests'));
  fs.copyFileSync(runner, path.join(fixture, 'scripts/run-tests.js'));
  fs.writeFileSync(path.join(fixture, 'package.json'), JSON.stringify({ version: '1.1.0' }));
  // Use the real package assertion with no dist: full mode must genuinely fail.
  fs.copyFileSync(path.join(__dirname, 'installer-package.test.js'), path.join(fixture, 'tests/installer-package.test.js'));
  for (const name of ['alpha.test.js', 'installer-package-extra.test.js', 'z-newly-discovered.test.js']) {
    fs.writeFileSync(path.join(fixture, 'tests', name), `console.log(${JSON.stringify('executed:' + name)});`);
  }
  fs.writeFileSync(path.join(fixture, 'tests/not-a-test.js'), 'throw new Error("Must not run helpers");');
  const run = (...args) => spawnSync(process.execPath, [path.join(fixture, 'scripts/run-tests.js'), ...args], { cwd: fixture, encoding: 'utf8' });
  const source = run('--source');
  assert.strictEqual(source.status, 0, source.stdout + source.stderr);
  assert(source.stdout.includes('Excluded: installer-package.test.js'), 'Source mode must print its exact exclusion');
  for (const name of ['alpha.test.js', 'installer-package-extra.test.js', 'z-newly-discovered.test.js'])
    assert(source.stdout.includes('executed:' + name), `Source mode must discover ${name}`);
  const full = run();
  assert.notStrictEqual(full.status, 0, 'Full mode must fail without real dist output');
  assert(full.stdout.includes('RUN installer-package.test.js'), 'Full mode must run the package test');
  assert(full.stderr.includes('ENOENT'), 'Failure must be the real missing dist, not a runner failure');
  assert(full.stdout.includes('executed:z-newly-discovered.test.js'), 'Full mode must run ALL tests even after a failure');
  assert(!full.stdout.includes('Excluded:'), 'Full mode must not exclude tests');
  fs.writeFileSync(path.join(fixture, 'tests/source-failure.test.js'), 'process.exit(7);');
  assert.notStrictEqual(run('--source').status, 0, 'Source mode must propagate source-test failures');
  assert.notStrictEqual(run('--unknown').status, 0, 'Unknown flags must not silently exclude tests');
} finally {
  fs.rmSync(fixture, { recursive: true, force: true });
}
const pkg = require(path.join(root, 'package.json'));
assert.strictEqual(pkg.scripts.test, 'node scripts/run-tests.js', 'npm test must run the complete discovered suite');
assert.strictEqual(pkg.scripts['test:source'], 'node scripts/run-tests.js --source', 'CI needs an explicit source-only command');
assert.strictEqual(pkg.scripts['release:local'], 'npm run package:release && npm test', 'Release must package before testing artifacts');
const workflow = fs.readFileSync(path.join(root, '.github/workflows/test.yml'), 'utf8');
assert(workflow.includes('actions/setup-dotnet@v4') && /dotnet-version: ['"]?9\.0\.x/.test(workflow), 'Windows CI must install the .NET 9 SDK');
assert(workflow.includes('run: npm run test:source'), 'Clean-checkout CI must run source-only tests');
console.log('Test runner discovery, exact exclusion, all-tests execution and failure propagation passed');
