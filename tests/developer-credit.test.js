const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const tray = fs.readFileSync(path.join(root, 'tray/SyncLightLite.Tray/Program.cs'), 'utf8');
const readme = fs.readFileSync(path.join(root, 'README.md'), 'utf8');
const pkg = require(path.join(root, 'package.json'));

assert(tray.includes('Tanoooki'), 'tray UI should credit Tanoooki');
assert(tray.includes('https://github.com/Tanoooki'), 'tray developer credit should link to GitHub');
assert(tray.includes('LinkLabel'), 'developer credit should be clickable');
assert(tray.includes('developerCredit'), 'developer credit should be localized');
assert(readme.includes('Tanoooki') && readme.includes('https://github.com/Tanoooki'), 'README should credit/link Tanoooki');
assert.strictEqual(pkg.scripts.test, 'node scripts/run-tests.js', 'canonical npm test must discover every test file (verified by test-runner.test.js)');
