const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const csproj = fs.readFileSync(path.join(root, 'tray/SyncLightLite.Tray/SyncLightLite.Tray.csproj'), 'utf8');
const program = fs.readFileSync(path.join(root, 'tray/SyncLightLite.Tray/Program.cs'), 'utf8');
const install = fs.readFileSync(path.join(root, 'scripts/install-startup.cmd'), 'utf8');

assert(fs.existsSync(path.join(root, 'assets/SyncLightLite.ico')), 'app icon .ico should exist');
assert(csproj.includes('assets') && csproj.includes('SyncLightLite.ico'), 'tray exe should embed the icon');
assert(install.includes('IconLocation'), 'Start Menu shortcuts should set IconLocation');
assert(install.includes('SyncLightLite.ico'), 'Start Menu shortcuts should use SyncLightLite.ico');
assert(program.includes('ToolTip'), 'settings UI should use ToolTip help');
assert(program.includes('HelpIcon('), 'settings rows should include ? help icons');
for (const term of ['Brilho', 'Saturação', 'Gamma', 'Threshold', 'Piso preto', 'Margem horizontal', 'Escala vertical', 'Ignorar cabeçalho']) {
  assert(program.includes(term), `help/UI should mention ${term}`);
}
