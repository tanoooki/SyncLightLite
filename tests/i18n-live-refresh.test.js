const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const tray = fs.readFileSync(path.join(root, 'tray/SyncLightLite.Tray/Program.cs'), 'utf8');

assert(tray.includes('ApplyLanguageToAllControls'), 'settings form should have a live language refresh method');
assert(tray.includes('_saveButton.Text = T("saveRestart")'), 'save button text should refresh when language changes');
assert(tray.includes('_titleLabel.Text') && tray.includes('_subtitleLabel.Text'), 'header labels should refresh when language changes');
assert(tray.includes('_rowLabels') && tray.includes('_rowHelp'), 'row labels/help should be tracked for live language refresh');
assert(tray.includes('RefreshLanguage();') && tray.includes('ApplyLanguageToAllControls();'), 'language selector change should immediately update visible controls');
assert(tray.includes('ApplyLanguageToTray();'), 'saved language should refresh tray menu/text');
assert(tray.includes('MenuTranslations'), 'tray menu should be localized');
assert(tray.includes('Settings') && tray.includes('Configurações') && tray.includes('Configuración') && tray.includes('设置'), 'tray/settings translations should include all supported languages');
