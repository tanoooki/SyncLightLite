const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const tray = fs.readFileSync(path.join(root, 'tray/SyncLightLite.Tray/Program.cs'), 'utf8');
const setup = fs.readFileSync(path.join(root, 'tray/SyncLightLite.Setup/Program.cs'), 'utf8');
const config = fs.readFileSync(path.join(root, 'config.json'), 'utf8');
const pkg = require(path.join(root, 'package.json'));

assert(config.includes('"language"'), 'default config should include language');
assert(tray.includes('public string Language'), 'tray config model should persist language');
assert(tray.includes('DetectLanguage'), 'tray should detect Windows UI culture for first-run/default language');
assert(setup.includes('DetectLanguage'), 'setup should seed config language from Windows UI culture during install');
assert(tray.includes('🇺🇸 English'), 'language selector should include English with flag');
assert(tray.includes('🇧🇷 Português'), 'language selector should include Brazilian Portuguese with flag');
assert(tray.includes('🇪🇸 Español'), 'language selector should include Spanish with flag');
assert(tray.includes('🇨🇳 中文'), 'language selector should include Chinese with flag');
assert(tray.includes('en') && tray.includes('pt-BR') && tray.includes('es') && tray.includes('zh-CN'), 'supported language codes should be present');
for (const token of ['Settings', 'Configurações', 'Configuración', '设置']) {
  assert(tray.includes(token), `tray translations should include ${token}`);
}
assert.strictEqual(pkg.scripts.test, 'node scripts/run-tests.js', 'canonical npm test must discover every test file (verified by test-runner.test.js)');
