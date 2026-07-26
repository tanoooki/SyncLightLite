const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const root = path.resolve(__dirname, '..');
const pkg = require(path.join(root, 'package.json'));
const version = pkg.version;
const stamp = version;
const dist = path.join(root, 'dist');
const out = path.join(dist, `installer-${stamp}`);
const payload = path.join(out, 'SyncLightLite');

function rm(p) { fs.rmSync(p, { recursive: true, force: true }); }
function mkdir(p) { fs.mkdirSync(p, { recursive: true }); }
function copy(src, dst) { fs.cpSync(src, dst, { recursive: true, force: true }); }
function write(p, s) { mkdir(path.dirname(p)); fs.writeFileSync(p, s); }
function exists(p) { return fs.existsSync(p); }

rm(out);
mkdir(payload);

console.log('[package] publishing tray app');
const trayOut = path.join(dist, `tray-${version}`);
rm(trayOut);
execFileSync('dotnet', ['publish', path.join(root, 'tray/SyncLightLite.Tray/SyncLightLite.Tray.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false', '-o', trayOut], { stdio: 'inherit' });

console.log('[package] publishing setup exe');
const setupOut = path.join(dist, `setup-${version}`);
rm(setupOut);
execFileSync('dotnet', ['publish', path.join(root, 'tray/SyncLightLite.Setup/SyncLightLite.Setup.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true', '-o', setupOut], { stdio: 'inherit' });

for (const rel of ['src', 'scripts', 'assets', 'calibration']) copy(path.join(root, rel), path.join(payload, rel));
for (const rel of ['package.json', 'config.json', 'README.md', 'LICENSE', 'CHANGELOG.md']) if (exists(path.join(root, rel))) copy(path.join(root, rel), path.join(payload, rel));
copy(trayOut, path.join(payload, 'dist/tray'));

const vendor = path.join(root, 'vendor/node_modules');
if (!exists(vendor)) throw new Error('vendor/node_modules is required for the release package');
copy(vendor, path.join(payload, 'vendor/node_modules'));

write(path.join(out, 'VERSION.txt'), version + '\n');
copy(path.join(setupOut, 'SyncLightLiteSetup.exe'), path.join(out, 'SyncLightLiteSetup.exe'));
write(path.join(out, 'README-INSTALL.txt'), `SyncLightLite ${version}

Run SyncLightLiteSetup.exe to install.
After installation, uninstall from Windows Settings > Apps > Installed Apps > SyncLightLite, or run Uninstall-SyncLightLite.exe from the installed folder.
`);

const zip = path.join(dist, `SyncLightLite-${version}-windows-x64.zip`);
rm(zip);
console.log('[package] compressing', zip);
const zipCommand = `Add-Type -AssemblyName System.IO.Compression.FileSystem; if (Test-Path '${zip.replace(/'/g, "''")}') { Remove-Item -Force '${zip.replace(/'/g, "''")}' }; [System.IO.Compression.ZipFile]::CreateFromDirectory('${out.replace(/'/g, "''")}', '${zip.replace(/'/g, "''")}')`;
execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', zipCommand], { stdio: 'inherit' });
console.log(`PACKAGE=${zip}`);
