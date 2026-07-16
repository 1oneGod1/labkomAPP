const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');
const assert = (condition, message) => {
  if (!condition) throw new Error(message);
};

const main = read('electron/main.js');
const preload = read('electron/preload.js');
const dialog = read('src/AdminExitDialog.jsx');
const app = read('src/App.jsx');
const attention = read('src/AttentionModeOverlay.jsx');

assert(
  main.includes("ipcMain.handle('verify-admin-exit-password'") &&
    main.includes("new URL('/api/admin/verify-password'") &&
    main.includes('loadServerConfig().serverUrl') &&
    main.includes("ipcMain.on('admin-exit-dialog-closed'") &&
    main.includes("applyWindowLayout('login')"),
  'Main process harus memverifikasi password lewat URL server tersimpan.',
);
assert(
  preload.includes("verifyAdminExitPassword: (pw) => ipcRenderer.invoke('verify-admin-exit-password', pw)") &&
    preload.includes("closeAdminExitDialog: () => ipcRenderer.send('admin-exit-dialog-closed')"),
  'Preload harus mengekspos IPC verifikasi keluar admin.',
);
assert(
  dialog.includes('verifyAdminExitPassword?.(password)') &&
    !dialog.includes('const SERVER_URL') &&
    !dialog.includes("from './api.js'") &&
    dialog.includes('z-[10000]'),
  'Dialog tidak boleh menyimpan target API statis di renderer.',
);
assert(
  app.indexOf('{showAdminDialog && (') > app.indexOf('const sharedOverlays = (') &&
    app.includes('allowAdminInteraction={showAdminDialog}') &&
    app.includes('closeAdminExitDialog?.()'),
  'Dialog keluar admin harus tersedia pada semua mode setelah setup.',
);
assert(
  attention.includes('if (!localEnabled || allowAdminInteraction) return;'),
  'Attention Mode harus melepas blok input saat dialog admin aktif.',
);

const configuredUrl = 'http://192.168.10.20:3001';
const target = new URL('/api/admin/verify-password', `${configuredUrl.replace(/\/$/, '')}/`);
assert(
  target.origin === configuredUrl && target.pathname === '/api/admin/verify-password',
  'Target verifikasi harus mempertahankan origin server LAN aktif.',
);

console.log('Admin exit flow: PASS');
