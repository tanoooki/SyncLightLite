const assert = require('assert');
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const live = fs.readFileSync(path.join(root, 'src/live.js'), 'utf8');
const hid = fs.readFileSync(path.join(root, 'src/hid.js'), 'utf8');
const program = fs.readFileSync(path.join(root, 'tray/SyncLightLite.Tray/Program.cs'), 'utf8');

assert(!live.includes("require('C:/tmp"), 'live.js should not hardcode extracted temp dependency path');
assert(!hid.includes("'C:/tmp"), 'hid.js should not hardcode extracted temp dependency path');
assert(live.includes('moduleRoot'), 'live.js should resolve vendor node_modules dynamically');
assert(hid.includes('moduleRoot'), 'hid.js should resolve vendor node_modules dynamically');
assert(program.includes('FindAppRoot'), 'tray app should find AppRoot dynamically');
assert(!program.includes('@"C:\Users\Pichau\SyncLightLite"'), 'tray app should not hardcode source AppRoot');
assert(!program.includes('Documents\SyncLightLite.png'), 'tray app should use bundled icon, not Documents PNG');
