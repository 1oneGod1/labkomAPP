const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..', '..');
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');
const assert = (condition, message) => {
  if (!condition) throw new Error(message);
};

const broadcaster = read('admin/src/components/ScreenShareAdmin.jsx');
const dashboard = read('admin/src/AdminDashboard.jsx');
const relay = read('server/src/realtimeHub.js');
const viewer = read('client/src/AdminScreenShare.jsx');
const clientMain = read('client/electron/main.js');

assert(
  broadcaster.includes('getDisplayMedia') &&
    broadcaster.includes('admin:screen-share-frame-v2') &&
    broadcaster.includes('blob.arrayBuffer()') &&
    broadcaster.includes('frameInFlightRef') &&
    broadcaster.includes("targetMode === 'all' ? 'all' : selectedPcs") &&
    broadcaster.includes('admin:screen-share-pause'),
  'Broadcaster Admin harus memakai frame biner, backpressure, target, dan pause.',
);
assert(
  relay.includes("socket.on('admin:screen-share-frame-v2'") &&
    relay.includes('admin_screen_binary_v1') &&
    relay.includes("frame.toString('base64')") &&
    relay.includes("socket.emit('presence:snapshot'") &&
    relay.includes("socket.on('admin:screen-share-pause'"),
  'Relay harus mendukung frame biner, fallback client lama, target, dan pause.',
);
assert(
  viewer.includes("socket.on('admin:screen-share-frame-v2'") &&
    viewer.includes('StableFrameImage') &&
    viewer.includes('probe.onload = publish') &&
    viewer.includes('lastSequenceRef') &&
    viewer.includes('URL.createObjectURL') &&
    viewer.includes('URL.revokeObjectURL') &&
    viewer.includes("socket.on('admin:screen-share-pause'"),
  'Viewer client harus decode sebelum swap, menolak frame usang, cleanup Object URL, dan mendukung pause.',
);
assert(
  clientMain.includes('admin_screen_binary_v1: true'),
  'Client harus mengiklankan kapabilitas transport biner.',
);

assert(
  dashboard.includes("socket.on('screen:update-v2'") &&
    dashboard.includes('screenFrameToBlob') &&
    dashboard.includes('StableScreenImage') &&
    dashboard.includes('probe.onload = publish') &&
    dashboard.includes('screenSequenceRef') &&
    dashboard.includes('URL.createObjectURL') &&
    dashboard.includes('focusedDisplayId') &&
    dashboard.includes('student_screen_binary_v1: true'),
  'Dashboard harus decode sebelum swap, menolak frame usang, menerima layar siswa binary, dan menyediakan pemilihan monitor.',
);
assert(
  relay.includes("socket.on('client:screen-v2'") &&
    relay.includes('validateStudentScreenFrame') &&
    relay.includes("candidate.volatile.emit('screen:update-v2'") &&
    relay.includes('display_id: selectedDisplays.at(-1)'),
  'Relay harus memvalidasi layar siswa binary dan meneruskan target monitor.',
);
assert(
  clientMain.includes("realtimeSocket.emit('client:screen-v2'") &&
    clientMain.includes('screen.getAllDisplays()') &&
    clientMain.includes('screenCaptureInFlight') &&
    clientMain.includes('multi_monitor_v1: true'),
  'Client harus memakai backpressure binary dan mengiklankan multi-monitor.',
);

console.log('Screen share v2 contract: PASS');
