const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const args = process.argv.slice(2);
if (args.length > 1 || (args.length === 1 && args[0] !== '--source')) {
  console.error('Usage: node scripts/run-tests.js [--source]');
  process.exit(1);
}
const root = path.resolve(__dirname, '..');
const tests = fs.readdirSync(path.join(root, 'tests'), { withFileTypes: true })
  .filter(entry => entry.isFile() && entry.name.endsWith('.test.js'))
  .map(entry => entry.name).sort();
const sourceOnly = args[0] === '--source';
if (sourceOnly) console.log('Excluded: installer-package.test.js (requires real package:release output)');
const selected = tests.filter(name => !sourceOnly || name !== 'installer-package.test.js');
if (selected.length === 0) {
  console.error('No tests discovered');
  process.exit(1);
}
let failed = 0;
for (const name of selected) {
  console.log(`RUN ${name}`);
  const result = spawnSync(process.execPath, [path.join(root, 'tests', name)], { cwd: root, stdio: 'inherit' });
  if (result.error) console.error(result.error);
  if (result.status !== 0) failed++;
}
console.log(`Tests: ${selected.length} run, ${selected.length - failed} passed, ${failed} failed`);
process.exitCode = failed ? 1 : 0;
