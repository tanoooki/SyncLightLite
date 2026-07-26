const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const tray = fs.readFileSync(path.join(root, 'tray/SyncLightLite.Tray/Program.cs'), 'utf8');
const pkg = require(path.join(root, 'package.json'));

const saveStart = tray.indexOf('private void SaveAndApply()');
assert(saveStart >= 0, 'settings form should have SaveAndApply');
const saveEnd = tray.indexOf('private static int Clamp', saveStart);
const saveBlock = tray.slice(saveStart, saveEnd);
assert(!saveBlock.includes('MessageBox.Show'), 'saving settings should not show a blocking popup');
assert(saveBlock.includes('ShowSavedPulse'), 'saving settings should trigger inline visual confirmation');
assert(tray.includes('private void ShowSavedPulse()'), 'settings form should implement save pulse feedback');
assert(tray.includes('Timer') && tray.includes('SavedPulse'), 'save feedback should use a timer-driven pulse');
assert(tray.includes('Color.FromArgb(46, 160, 67)') || tray.includes('Success'), 'save feedback should use a green success color');
for (const token of ['savedShort', 'Salvo', 'Saved', 'Guardado', '已保存']) {
  assert(tray.includes(token), `save feedback should include ${token}`);
}
assert(pkg.scripts.test.includes('save-feedback.test.js'), 'canonical npm test should run save feedback test');
