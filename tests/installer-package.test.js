const assert = require('assert');
const fs = require('fs');
const path = require('path');
const projectRoot = path.resolve(__dirname, '..');
const pkg = require(path.join(projectRoot, 'package.json'));
const dist = path.join(projectRoot, 'dist');
const installers = fs.readdirSync(dist).filter(n => n.startsWith('installer-')).sort();
assert(installers.length, 'at least one versioned installer folder should exist');
const packageRoot = path.join(dist, installers.includes(`installer-${pkg.version}`) ? `installer-${pkg.version}` : installers.at(-1));

assert(fs.existsSync(path.join(packageRoot, 'SyncLightLiteSetup.exe')), 'release package should expose a friendly .exe installer at the root');
assert(!fs.existsSync(path.join(packageRoot, 'Install-SyncLightLite.cmd')), 'release package should not ask users to run a .cmd installer');
assert(!fs.existsSync(path.join(packageRoot, 'Install-SyncLightLite.ps1')), 'release package should not ask users to run a .ps1 installer');

assert(fs.existsSync(path.join(projectRoot, 'tray/SyncLightLite.Setup/SyncLightLite.Setup.csproj')), 'setup exe project should exist');
const setupSrc = fs.readFileSync(path.join(projectRoot, 'tray/SyncLightLite.Setup/Program.cs'), 'utf8');
assert(setupSrc.includes('Windows\\CurrentVersion\\Uninstall\\SyncLightLite'), 'setup exe should register SyncLightLite under Windows Installed Apps');
assert(setupSrc.includes('DisplayName') && setupSrc.includes('UninstallString') && setupSrc.includes('DisplayVersion'), 'installed-app registry entry should include display name, uninstall command and version');
assert(setupSrc.includes('SyncLightLite.lnk'), 'setup exe should create .lnk shortcuts');
assert(setupSrc.includes('--minimized'), 'startup shortcut should launch tray exe minimized');
assert(setupSrc.includes('SyncLightLite.cmd'), 'setup exe should remove old Startup .cmd entries from prior versions');
assert(setupSrc.includes('Uninstall-SyncLightLite.exe'), 'setup exe should install an exe uninstaller copy');
assert(setupSrc.includes('StartInstalledApp') && setupSrc.includes('if (!quiet) StartInstalledApp();'), 'interactive setup should launch SyncLightLite after install');

assert(fs.existsSync(path.join(packageRoot, 'SyncLightLite/src/live.js')), 'package should contain live engine');
assert(fs.existsSync(path.join(packageRoot, 'SyncLightLite/vendor/node_modules/node-hid')), 'package should vendor node-hid');
assert(fs.existsSync(path.join(packageRoot, 'SyncLightLite/vendor/node_modules/@warren-robobloq/quiklight')), 'package should vendor quiklight');
