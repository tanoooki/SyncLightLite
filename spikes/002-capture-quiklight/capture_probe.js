const q = require('C:/tmp/synclight_asar/extract/node_modules/@warren-robobloq/quiklight');
const monitors = q.getMonitors();
let count = 0;
console.log('monitors', JSON.stringify(monitors));
q.startCaptureMultiScreens((frame) => {
  count++;
  console.log('frame', count, JSON.stringify({ keys: Object.keys(frame || {}), sample: frame && frame[monitors[0].displayId] ? frame[monitors[0].displayId].slice(0, 30) : frame }).slice(0, 1000));
  if (count >= 3) {
    q.stopCaptureMultiScreens();
    setTimeout(() => process.exit(0), 100);
  }
}, monitors, 120, 10);
setTimeout(() => { console.error('timeout'); try { q.stopCaptureMultiScreens(); } catch(e) {} process.exit(2); }, 5000);
