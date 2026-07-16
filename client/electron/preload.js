const { contextBridge, ipcRenderer } = require('electron');

// Expose API yang aman ke renderer (React) via window.electronAPI
contextBridge.exposeInMainWorld('electronAPI', {
  // ── Read ────────────────────────────────────────────────────────
  getPcName: () => ipcRenderer.invoke('get-pc-name'),

  // ── Konfigurasi URL server (disimpan di userData) ───────────────
  getServerUrl:  ()    => ipcRenderer.invoke('get-server-url'),
  saveServerUrl: (url) => ipcRenderer.send('save-server-url', url),

  // ── Login / Logout IPC ──────────────────────────────────────────
  loginSuccess:  (studentData) => ipcRenderer.send('login-success', studentData),
  doLogout:      ()            => ipcRenderer.send('do-logout'),

  // ── Resize widget: 'minimized' | 'regular' | 'expanded' ────────
  resizeWindow:  (mode)        => ipcRenderer.send('resize-window', mode),

  // ── Admin: keluar aplikasi setelah password terverifikasi ───────
  quitApp: () => ipcRenderer.send('quit-app'),

  // ── Keluar dari layar setup (sebelum konfigurasi) ───────────────
  exitApp: () => ipcRenderer.send('exit-app'),

  // ── Listener dari Main → Renderer ──────────────────────────────
  onKioskOff:        (cb) => ipcRenderer.on('kiosk-off',       (_e, data) => cb(data)),
  onReturnToLogin:   (cb) => ipcRenderer.on('return-to-login', ()         => cb()),
  // Update sudah didownload di background, siap install saat keluar
  onUpdateDownloaded:(cb) => ipcRenderer.on('update-downloaded', ()       => cb()),

  removeAllListeners: (channel) => ipcRenderer.removeAllListeners(channel),

  // ── Server auto-discovery via UDP broadcast ─────────────────────
  onServerDiscovered: (cb) => ipcRenderer.on('server-discovered', (_e, data) => cb(data)),

  // ── Trigger dialog keluar dari main process (globalShortcut) ────
  onShowAdminDialog: (cb) => ipcRenderer.on('show-admin-dialog', () => cb()),

  // ── Verify server reachable dari main process (bukan renderer) ────
  verifyServer: (url) => ipcRenderer.invoke('verify-server', url),

  // ── General API request via Node.js http (bypass Chromium fetch restriction) ──
  apiRequest: (url, options) => ipcRenderer.invoke('api-request', url, options),

  // ── Attention Mode: paksa kiosk + keyboard hook saat enabled ───
  setAttentionMode: (enabled) => ipcRenderer.send('set-attention-mode', enabled),

  // ── Device token untuk socket auth (di-issue oleh main process) ──
  getClientToken: () => ipcRenderer.invoke('get-client-token'),

  // ── Verify emergency password tanpa expose plaintext ke renderer ──
  verifyEmergencyPassword: (pw) => ipcRenderer.invoke('verify-emergency-password', pw),

  // Verifikasi password admin online memakai URL server tersimpan di main process.
  verifyAdminExitPassword: (pw) => ipcRenderer.invoke('verify-admin-exit-password', pw),
  closeAdminExitDialog: () => ipcRenderer.send('admin-exit-dialog-closed'),
});
