const { app, BrowserWindow, globalShortcut, ipcMain, screen, desktopCapturer } = require('electron');
const os              = require('os');
const path            = require('path');
const fs              = require('fs');
const http            = require('http');
const dgram           = require('dgram');
const crypto          = require('crypto');
const { execSync, spawn } = require('child_process');
const { io }          = require('socket.io-client');
const ActivityMonitor = require('./activityMonitor');

// Semua HTTP renderer sudah lewat IPC apiRequest (file:// → main process Node.js http),
// jadi flag disable-web-security tidak diperlukan. Socket.io WebSocket dari file:// ke
// http:// LAN tetap diizinkan Chromium, dan server CORS sudah allow null origin.

// ── Single instance: hanya aktif di production supaya dev client bisa jalan berdampingan
if (app.isPackaged && !app.requestSingleInstanceLock()) {
  app.quit();
}

// ─── UDP Discovery Listener ─────────────────────────────────────────────────
const DISCOVERY_PORT = 41234;
let   udpSocket      = null;

function startDiscoveryListener() {
  if (udpSocket) return;
  udpSocket = dgram.createSocket({ type: 'udp4', reuseAddr: true });
  udpSocket.bind(DISCOVERY_PORT, () => {
    udpSocket.addMembership && undefined; // ipv4 broadcast, no multicast needed
    console.log('[DISCOVERY] Listening for server broadcasts on port', DISCOVERY_PORT);
  });
  udpSocket.on('message', (msg) => {
    try {
      const data = JSON.parse(msg.toString());
      if (data.labkom && data.ip && data.port) {
        if (mainWindow && !mainWindow.isDestroyed()) {
          mainWindow.webContents.send('server-discovered', {
            url:  `http://${data.ip}:${data.port}`,
            name: data.name || 'LabKom Admin',
            ip:   data.ip,
            port: data.port,
          });
        }
      }
    } catch {}
  });
  udpSocket.on('error', (err) => {
    console.warn('[DISCOVERY] error:', err.message);
  });
}

function stopDiscoveryListener() {
  if (udpSocket) { try { udpSocket.close(); } catch {} udpSocket = null; }
}
const { autoUpdater } = require('electron-updater');
const log             = require('electron-log');

const isDev = process.env.NODE_ENV === 'development' || !app.isPackaged;
const allowDevTools = process.env.OPEN_ELECTRON_DEVTOOLS === '1';
let allowAppQuit = false;
let realtimeSocket = null;
let presenceHeartbeatTimer = null;
let serverCapabilities = {
  student_screen_binary_v1: false,
  multi_monitor_v1: false,
};

// ── Auto-Updater (silent background update) ──────────────────────────────────
// Client: download otomatis di background, install saat app keluar
autoUpdater.logger         = log;
autoUpdater.logger.transports.file.level = 'info';
autoUpdater.autoDownload   = true;   // Langsung download kalau ada update
autoUpdater.autoInstallOnAppQuit = true;  // Install otomatis saat app ditutup/restart

autoUpdater.on('update-downloaded', () => {
  log.info('[CLIENT UPDATE] Update didownload, akan diinstall saat app ditutup.');
  // Beritahu renderer agar tampil notifikasi kecil (opsional)
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send('update-downloaded');
  }
});
autoUpdater.on('error', (err) => {
  log.warn('[CLIENT UPDATE] Error (diabaikan):', err.message);
});

process.on('uncaughtException', (err) => {
  log.error('[MAIN] uncaughtException:', err && err.stack ? err.stack : err);
});

process.on('unhandledRejection', (reason) => {
  log.error('[MAIN] unhandledRejection:', reason);
});

// ── Path file konfigurasi server URL ────────────────────────────────────────
function getConfigPath() {
  return path.join(app.getPath('userData'), 'server.config.json');
}
function loadServerConfig() {
  try {
    const raw = fs.readFileSync(getConfigPath(), 'utf-8');
    return JSON.parse(raw);
  } catch { return {}; }
}
function isAllowedLabServerUrl(value) {
  try {
    const parsed = new URL(value);
    if (parsed.protocol !== 'http:') return false;
    const host = parsed.hostname.toLowerCase();
    if (host === 'localhost' || host === '127.0.0.1') return true;
    if (/^10\.(\d{1,3}\.){2}\d{1,3}$/.test(host)) return true;
    if (/^192\.168\.(\d{1,3}\.)\d{1,3}$/.test(host)) return true;
    const match172 = host.match(/^172\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/);
    return Boolean(match172 && Number(match172[1]) >= 16 && Number(match172[1]) <= 31);
  } catch {
    return false;
  }
}
function saveServerConfig(data) {
  fs.writeFileSync(getConfigPath(), JSON.stringify(data, null, 2), 'utf-8');
}

// ── Device identity & token (per-PC client auth) ────────────────────────────────
function getDevicePath() {
  return path.join(app.getPath('userData'), 'device.json');
}
function loadDeviceInfo() {
  try {
    const raw = fs.readFileSync(getDevicePath(), 'utf-8');
    return JSON.parse(raw);
  } catch { return {}; }
}
function saveDeviceInfo(info) {
  try { fs.writeFileSync(getDevicePath(), JSON.stringify(info, null, 2), 'utf-8'); } catch (_) {}
}
function getOrCreateDeviceId() {
  const info = loadDeviceInfo();
  if (info.device_id) return info.device_id;
  const id = crypto.randomBytes(16).toString('hex');
  saveDeviceInfo({ ...info, device_id: id });
  return id;
}
function getStoredClientToken() {
  return loadDeviceInfo().client_token || null;
}
function setStoredClientToken(token) {
  const info = loadDeviceInfo();
  info.client_token = token;
  saveDeviceInfo(info);
}

// Minta token dari server. Resolve null kalau gagal.
function requestDeviceToken(serverUrl) {
  return new Promise((resolve) => {
    if (!serverUrl) return resolve(null);
    let parsed;
    try { parsed = new URL(`${serverUrl}/api/auth/device-register`); }
    catch { return resolve(null); }
    const body = JSON.stringify({
      device_id: getOrCreateDeviceId(),
      pc_name: os.hostname(),
    });
    const headers = { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) };
    if (process.env.LABKOM_CLIENT_REGISTRATION_KEY) {
      headers['X-LabKom-Registration-Key'] = process.env.LABKOM_CLIENT_REGISTRATION_KEY;
    }
    const req = http.request({
      hostname: parsed.hostname,
      port: parseInt(parsed.port) || 3001,
      path: parsed.pathname,
      method: 'POST',
      headers,
    }, (res) => {
      let buf = '';
      res.on('data', (d) => buf += d);
      res.on('end', () => {
        try {
          const json = JSON.parse(buf);
          if (json?.success && json.data?.token) {
            setStoredClientToken(json.data.token);
            resolve(json.data.token);
          } else {
            log.warn('[DEVICE-AUTH] Register ditolak:', json?.message);
            resolve(null);
          }
        } catch { resolve(null); }
      });
    });
    req.setTimeout(5000, () => { req.destroy(); resolve(null); });
    req.on('error', (err) => { log.warn('[DEVICE-AUTH] Error:', err.message); resolve(null); });
    req.write(body);
    req.end();
  });
}

// Pastikan ada token valid; kalau belum atau ditolak server, register ulang
async function ensureClientToken(serverUrl) {
  const stored = getStoredClientToken();
  if (stored) return stored;
  return await requestDeviceToken(serverUrl);
}

let mainWindow;
let focusRecoveryTimer = null;
let aggressiveFocusInterval = null;
let screenShareTimer   = null;
let screenCaptureInFlight = false;
let screenCaptureSequence = 0;
let lockModeEnabled = true;

// ── Windows Keyboard Hook (blokir Alt+Tab di level OS) ────────────────────────────────
let kbHookProcess = null;
let kbHookFlagPath = null;
let kbHookReady = false;
let kbHookReadyTimer = null;
let kbHookRestartTimer = null;

function clearKeyboardHookTimers() {
  if (kbHookReadyTimer) {
    clearTimeout(kbHookReadyTimer);
    kbHookReadyTimer = null;
  }
  if (kbHookRestartTimer) {
    clearTimeout(kbHookRestartTimer);
    kbHookRestartTimer = null;
  }
}

function scheduleKeyboardHookRestart(reason) {
  if (allowAppQuit || !isKioskLocked() || kbHookRestartTimer) return;
  log.warn(`[KIOSK] Menjadwalkan ulang keyboard hook: ${reason}`);
  kbHookRestartTimer = setTimeout(() => {
    kbHookRestartTimer = null;
    startKeyboardHook();
  }, 1000);
}

function startKeyboardHook() {
  if (process.platform !== 'win32') return;
  if (kbHookProcess) return;

  try {
    const ps1Path = app.isPackaged
      ? path.join(process.resourcesPath, 'electron', 'blockAltTab.ps1')
      : path.join(__dirname, 'blockAltTab.ps1');

    if (!fs.existsSync(ps1Path)) {
      log.error('[KIOSK] blockAltTab.ps1 tidak ditemukan di:', ps1Path);
      scheduleKeyboardHookRestart('script-tidak-ditemukan');
      return;
    }

    kbHookFlagPath = path.join(app.getPath('userData'), 'kbhook-stop.flag');
    try { if (fs.existsSync(kbHookFlagPath)) fs.unlinkSync(kbHookFlagPath); } catch {}

    kbHookReady = false;
    const hookProcess = spawn('powershell.exe', [
      '-NoLogo',
      '-NoProfile',
      '-NonInteractive',
      '-WindowStyle', 'Hidden',
      '-ExecutionPolicy', 'Bypass',
      '-File', ps1Path,
      '-FlagFile', kbHookFlagPath,
      '-ElectronPID', String(process.pid),
    ], {
      windowsHide: true,
      detached: false,
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    kbHookProcess = hookProcess;

    hookProcess.stdout?.on('data', (data) => {
      const message = data.toString().trim();
      if (!message) return;
      log.info('[KBHOOK stdout]', message);
      if (message.includes('Hook terpasang berhasil')) {
        kbHookReady = true;
        if (kbHookReadyTimer) {
          clearTimeout(kbHookReadyTimer);
          kbHookReadyTimer = null;
        }
      }
    });
    hookProcess.stderr?.on('data', (data) => {
      const message = data.toString().trim();
      if (message) log.warn('[KBHOOK stderr]', message);
    });

    hookProcess.on('exit', (code) => {
      if (kbHookProcess === hookProcess) kbHookProcess = null;
      kbHookReady = false;
      if (kbHookReadyTimer) {
        clearTimeout(kbHookReadyTimer);
        kbHookReadyTimer = null;
      }
      log.info('[KIOSK] Keyboard hook process keluar, kode:', code);
      scheduleKeyboardHookRestart(`process-exit-${code}`);
    });

    hookProcess.on('error', (err) => {
      if (kbHookProcess === hookProcess) kbHookProcess = null;
      kbHookReady = false;
      log.error('[KIOSK] Gagal menjalankan keyboard hook:', err.message);
      scheduleKeyboardHookRestart('spawn-error');
    });

    kbHookReadyTimer = setTimeout(() => {
      kbHookReadyTimer = null;
      if (kbHookProcess !== hookProcess || kbHookReady || !isKioskLocked()) return;
      log.error('[KIOSK] Keyboard hook tidak siap dalam 5 detik; proses akan dimulai ulang.');
      kbHookProcess = null;
      try { hookProcess.kill(); } catch {}
      scheduleKeyboardHookRestart('ready-timeout');
    }, 5000);

    log.info('[KIOSK] Memulai Windows keyboard hook (PID:', hookProcess.pid, '), script:', ps1Path);
  } catch (err) {
    kbHookProcess = null;
    kbHookReady = false;
    log.error('[KIOSK] Error saat memulai keyboard hook:', err.message);
    scheduleKeyboardHookRestart('start-exception');
  }
}

function stopKeyboardHook() {
  if (process.platform !== 'win32') return;

  clearKeyboardHookTimers();
  kbHookReady = false;

  if (kbHookFlagPath) {
    try { fs.writeFileSync(kbHookFlagPath, 'stop', 'utf-8'); } catch {}
  }

  const hookProcess = kbHookProcess;
  kbHookProcess = null;
  if (hookProcess) {
    setTimeout(() => {
      if (!hookProcess.killed) {
        try { hookProcess.kill(); } catch {}
      }
    }, 1000);
  }

  log.info('[KIOSK] Windows keyboard hook dihentikan');
}
// ── Windows Taskbar Hide/Show (sembunyikan taskbar saat lock) ────────────────────────────────
let taskbarHideScript = null;

function getTaskbarScriptPath() {
  if (taskbarHideScript) return taskbarHideScript;
  const scriptDir = app.isPackaged
    ? path.join(path.dirname(process.execPath), 'resources', 'electron')
    : __dirname;
  taskbarHideScript = path.join(app.getPath('userData'), 'taskbar-ctl.ps1');
  // Tulis script sekali
  const ps1 = `
param([string]$Action = "hide")
Add-Type -Name TBCtl -Namespace Win32 -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string wnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
'@
$sw = if ($Action -eq "show") { 5 } else { 0 }
$h = [Win32.TBCtl]::FindWindow("Shell_TrayWnd","")
if ($h -ne [IntPtr]::Zero) { [Win32.TBCtl]::ShowWindow($h, $sw) | Out-Null }
$h2 = [Win32.TBCtl]::FindWindow("Shell_SecondaryTrayWnd","")
if ($h2 -ne [IntPtr]::Zero) { [Win32.TBCtl]::ShowWindow($h2, $sw) | Out-Null }
`;
  try { fs.writeFileSync(taskbarHideScript, ps1, 'utf-8'); } catch {}
  return taskbarHideScript;
}

function hideTaskbar() {
  if (process.platform !== 'win32') return;
  try {
    const script = getTaskbarScriptPath();
    spawn('powershell.exe', [
      '-NoProfile', '-NonInteractive', '-WindowStyle', 'Hidden',
      '-ExecutionPolicy', 'Bypass', '-File', script, '-Action', 'hide'
    ], { detached: true, stdio: 'ignore' });
  } catch {}
  log.info('[KIOSK] Taskbar disembunyikan');
}

function showTaskbar() {
  if (process.platform !== 'win32') return;
  try {
    const script = getTaskbarScriptPath();
    execSync(`powershell -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "${script}" -Action show`, { timeout: 5000 });
  } catch {}
  log.info('[KIOSK] Taskbar ditampilkan kembali');
}
const screenShareState = {
  active:      false,
  lastErrorAt: 0,
  serverUrl:   null,
  studentName: null,
  pcName:      os.hostname(),
};

//  Activity Monitor Instance
let activityMonitor = null;
let activeSessionId = null;
const CAPTURE_PROFILES = {
  overview: {
    mode: 'overview',
    width: 480,
    height: 270,
    jpegQuality: 40,
    intervalMs: 1000,
  },
  focus: {
    mode: 'focus',
    width: 1280,
    height: 720,
    jpegQuality: 65,
    intervalMs: 250,
  },
};
let captureProfileMode = 'overview';
let captureDisplayId = null;

// ── Ukuran widget per mode ───────────────────────────────────────
const SIZES = {
  minimized:  { width: 300, height: 72  },
  regular:    { width: 340, height: 430 },
  expanded:   { width: 400, height: 560 },
  checklist:  { width: 780, height: 840 },  // Untuk form checklist pre/post sesi
};

function getTopRight(w, h) {
  const { width: sw } = screen.getPrimaryDisplay().workAreaSize;
  return { x: sw - w - 20, y: 20 };
}

function getCenter(w, h) {
  const { width: sw, height: sh } = screen.getPrimaryDisplay().workAreaSize;
  return { x: Math.round((sw - w) / 2), y: Math.round((sh - h) / 2) };
}

function isKioskLocked() {
  // Gunakan state yang diinginkan, bukan state window sesaat. Saat Alt+Tab atau
  // shell Windows memaksa keluar fullscreen, isFullScreen() dapat sempat false.
  return Boolean(lockModeEnabled && mainWindow && !mainWindow.isDestroyed());
}

function keepWindowVisible() {
  if (!mainWindow || mainWindow.isDestroyed()) return;

  try {
    // Set always on top dengan level tertinggi
    mainWindow.setAlwaysOnTop(true, 'screen-saver', 1);
    mainWindow.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true });

    if (isKioskLocked()) {
      // Mode kiosk: super aggressive focus recovery
      mainWindow.setKiosk(true);
      mainWindow.setFullScreen(true);
      mainWindow.restore();
      mainWindow.show();
      mainWindow.focus();
      mainWindow.moveTop();

      // Force focus dengan teknik ganda
      if (process.platform === 'win32') {
        mainWindow.blur();
        mainWindow.focus();

        // Tambahan: set level always on top lebih tinggi
        mainWindow.setAlwaysOnTop(false);
        mainWindow.setAlwaysOnTop(true, 'screen-saver', 1);
      }
      return;
    }

    // Mode widget: recovery normal
    if (mainWindow.isMinimized()) mainWindow.restore();
    if (typeof mainWindow.showInactive === 'function') {
      mainWindow.showInactive();
    } else {
      mainWindow.show();
    }
    mainWindow.moveTop();
  } catch (_) {}
}

function scheduleFocusRecovery(delay = 50) {
  if (focusRecoveryTimer) clearTimeout(focusRecoveryTimer);
  focusRecoveryTimer = setTimeout(() => {
    focusRecoveryTimer = null;
    keepWindowVisible();
  }, delay);
}

function preventUnexpectedQuit(event) {
  if (allowAppQuit) return;
  if (!mainWindow || mainWindow.isDestroyed()) return;
  event.preventDefault();
  log.warn('[APP] Quit dicegah karena tidak berasal dari jalur resmi.');
  scheduleFocusRecovery(0);
}

function requestControlledQuit(reason) {
  allowAppQuit = true;
  log.info(`[APP] Controlled quit: ${reason}`);
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.setClosable(true);
    mainWindow.setKiosk(false);
  }
  stopAggressiveFocusLoop();
  stopKeyboardHook();
  showTaskbar(); // Selalu pulihkan taskbar saat quit
  globalShortcut.unregisterAll();
  app.quit();
}

function startAggressiveFocusLoop() {
  if (aggressiveFocusInterval) return;

  // Loop yang terus-menerus memaksa window tetap di depan saat lock mode
  // Interval 50ms: sangat agresif agar user tidak sempat lihat jendela lain
  aggressiveFocusInterval = setInterval(() => {
    if (!mainWindow || mainWindow.isDestroyed()) return;

    if (isKioskLocked()) {
      // Pastikan window selalu di depan dan fullscreen
      if (!mainWindow.isFocused()) {
        keepWindowVisible();
      }
      // Pastikan kiosk dan fullscreen tetap aktif
      if (!mainWindow.isKiosk()) mainWindow.setKiosk(true);
      if (!mainWindow.isFullScreen()) mainWindow.setFullScreen(true);
    }
  }, 50); // Setiap 50ms cek dan pulihkan fokus

  log.info('[KIOSK] Aggressive focus loop dimulai');
}

function stopAggressiveFocusLoop() {
  if (!aggressiveFocusInterval) return;

  clearInterval(aggressiveFocusInterval);
  aggressiveFocusInterval = null;
  log.info('[KIOSK] Aggressive focus loop dihentikan');
}

let currentLayoutMode = 'login';
let attentionModeOn = false;
let preAttentionLayoutMode = null;
let preAdminExitLayoutMode = null;

function applyWindowLayout(mode = 'regular') {
  if (!mainWindow || mainWindow.isDestroyed()) return;
  currentLayoutMode = mode;

  const isLoginLayout = mode === 'login';
  lockModeEnabled = isLoginLayout;
  mainWindow.setResizable(true);

  if (isLoginLayout) {
    mainWindow.setBounds(screen.getPrimaryDisplay().bounds, true);
    mainWindow.setKiosk(true);
    mainWindow.setFullScreen(true);

    // Mulai aggressive focus loop dan keyboard hook untuk mode lock
    startAggressiveFocusLoop();
    startKeyboardHook();
    hideTaskbar();
  } else {
    const size = SIZES[mode] || SIZES.regular;
    const { x, y } = mode === 'checklist'
      ? getCenter(size.width, size.height)
      : getTopRight(size.width, size.height);

    mainWindow.setKiosk(false);
    mainWindow.setFullScreen(false);
    mainWindow.setBounds({ x, y, width: size.width, height: size.height }, true);

    // Hentikan aggressive focus loop dan keyboard hook untuk mode widget
    stopAggressiveFocusLoop();
    stopKeyboardHook();
    showTaskbar();
  }

  mainWindow.setResizable(false);
  mainWindow.setAlwaysOnTop(true, 'screen-saver');
  mainWindow.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true });
  mainWindow.setSkipTaskbar(true);
  keepWindowVisible();
}

function logScreenWarning(message) {
  const now = Date.now();
  if (now - screenShareState.lastErrorAt < 15000) return;
  screenShareState.lastErrorAt = now;
  log.warn(message);
}

function getCaptureProfile() {
  return CAPTURE_PROFILES[captureProfileMode] || CAPTURE_PROFILES.overview;
}

function getMonitorMetadata() {
  const primaryDisplayId = String(screen.getPrimaryDisplay().id);
  return screen.getAllDisplays().slice(0, 8).map((display, index) => ({
    display_id: String(display.id),
    label: `Monitor ${index + 1}${String(display.id) === primaryDisplayId ? ' (Utama)' : ''}`,
    width: display.size.width,
    height: display.size.height,
    scale_factor: display.scaleFactor,
    primary: String(display.id) === primaryDisplayId,
  }));
}

function restartScreenShareLoop() {
  if (!screenShareState.active) return;

  if (screenShareTimer) {
    clearInterval(screenShareTimer);
    screenShareTimer = null;
  }

  const profile = getCaptureProfile();
  screenShareTimer = setInterval(() => {
    postScreenshot();
  }, profile.intervalMs);
}

function applyCaptureProfile(mode = 'overview', displayId = null) {
  const nextMode = CAPTURE_PROFILES[mode] ? mode : 'overview';
  const nextDisplayId = nextMode === 'focus' && displayId != null
    ? String(displayId).slice(0, 64)
    : null;
  if (captureProfileMode === nextMode && captureDisplayId === nextDisplayId) return;

  captureProfileMode = nextMode;
  captureDisplayId = nextDisplayId;
  log.info(`[SCREEN] Capture profile -> ${nextMode}, monitor -> ${nextDisplayId || 'primary'}`);
  restartScreenShareLoop();

  if (screenShareState.active) {
    postScreenshot();
  }
}

function createWindow() {
  mainWindow = new BrowserWindow({
    // ── Kiosk & tampilan ─────────────────────────────────────────
    kiosk:          true,   // Full screen mutlak, tutupi taskbar
    fullscreen:     true,
    alwaysOnTop:    true,
    frame:          false,  // Hapus title bar / window border
    transparent:    true,   // Background OS transparan →’ widget melayang
    skipTaskbar:    true,   // Sembunyikan dari taskbar Windows
    movable:        false,
    autoHideMenuBar:true,
    hasShadow:      true,

    // ── Nonaktifkan close akibat tombol ───────────────────────────
    closable:       false,
    minimizable:    false,
    maximizable:    false,
    resizable:      false,

    // ── Keamanan Electron ────────────────────────────────────────
    webPreferences: {
      preload:           path.join(__dirname, 'preload.js'),
      contextIsolation:  true,
      nodeIntegration:   false,
      devTools:          allowDevTools,
      spellcheck:        false,
    },
  });
  mainWindow.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true });
  mainWindow.setMenuBarVisibility(false);
  mainWindow.removeMenu();

  // ── Load URL ─────────────────────────────────────────────────
  if (isDev) {
    mainWindow.loadURL('http://localhost:5173');
    if (allowDevTools) {
      mainWindow.webContents.openDevTools({ mode: 'detach' });
    }
  } else {
    mainWindow.loadFile(path.join(__dirname, '../dist/index.html'));
  }

  // ── Cegah navigasi keluar ─────────────────────────────────────
  mainWindow.webContents.on('will-navigate', (e, url) => {
    if (!url.startsWith('http://localhost:5173') && !url.startsWith('file://')) {
      e.preventDefault();
    }
  });

  // ── Cegah buka jendela baru ───────────────────────────────────
  mainWindow.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));

  // ── Blokir Alt+F4 dan shortcut berbahaya di level webContents ────────────────────────────────
  // globalShortcut TIDAK bisa menangkap Alt+F4 saat window fokus di Windows.
  // before-input-event menangkap keystroke SEBELUM Chromium/OS memprosesnya.
  mainWindow.webContents.on('before-input-event', (event, input) => {
    if (allowAppQuit) return;

    const alt  = input.alt;
    const ctrl = input.control;
    const meta = input.meta;
    const key  = (input.key || '').toLowerCase();

    // Blokir Alt+F4
    if (alt && key === 'f4') { event.preventDefault(); return; }
    // Blokir Ctrl+W (close tab/window)
    if (ctrl && key === 'w') { event.preventDefault(); return; }
    // Blokir Ctrl+F4
    if (ctrl && key === 'f4') { event.preventDefault(); return; }
    // Blokir Ctrl+Shift+Esc (Task Manager)
    if (ctrl && input.shift && key === 'escape') { event.preventDefault(); return; }
    // Blokir F11 (toggle fullscreen)
    if (!ctrl && !alt && key === 'f11') { event.preventDefault(); return; }
    // Blokir Alt+Esc
    if (alt && key === 'escape') { event.preventDefault(); return; }
    // Blokir Win key combinations
    if (meta) { event.preventDefault(); return; }
  });

  mainWindow.on('close', (event) => {
    if (allowAppQuit) return;
    event.preventDefault();
    // Paksa tampil kembali
    if (mainWindow && !mainWindow.isDestroyed()) {
      mainWindow.show();
      mainWindow.focus();
    }
    scheduleFocusRecovery(0);
  });

  // Low-level hook memblokir shortcut sistem; recovery fokus ini menjadi fallback
  // jika Windows sempat memindahkan fokus atau shell dimulai ulang.
  mainWindow.on('blur', () => {
    if (isKioskLocked()) {
      // Mode login: recovery sangat agresif
      scheduleFocusRecovery(10);
    } else {
      // Mode widget: masih perlu recovery tapi lebih lembut
      scheduleFocusRecovery(30);
    }
  });
  mainWindow.on('minimize', (event) => {
    event.preventDefault();
    scheduleFocusRecovery(5);
  });
  mainWindow.on('hide', () => scheduleFocusRecovery(5));
  mainWindow.on('restore', () => scheduleFocusRecovery(10));
  mainWindow.on('leave-full-screen', () => {
    if (isKioskLocked()) {
      scheduleFocusRecovery(0);
      mainWindow.setKiosk(true);
      mainWindow.setFullScreen(true);
    }
  });
  mainWindow.on('show', () => {
    if (isKioskLocked()) keepWindowVisible();
  });
  mainWindow.on('focus', () => {
    if (isKioskLocked()) {
      mainWindow.setKiosk(true);
      mainWindow.setFullScreen(true);
    }
  });
  // Cegah unfocus via Alt+Tab dengan aggressive recovery
  mainWindow.on('will-resize', (event) => {
    if (isKioskLocked()) event.preventDefault();
  });
  mainWindow.on('move', (event) => {
    if (isKioskLocked()) event.preventDefault();
  });
  // Start keyboard hook immediately on launch (kiosk starts in login mode)
  startAggressiveFocusLoop();
  startKeyboardHook();
  hideTaskbar();

  mainWindow.webContents.on('render-process-gone', (_event, details) => {
    log.error('[WINDOW] render-process-gone:', details);
    setTimeout(() => {
      if (!mainWindow || mainWindow.isDestroyed()) createWindow();
      else mainWindow.reload();
    }, 1000);
  });
  mainWindow.webContents.on('unresponsive', () => {
    log.warn('[WINDOW] Renderer unresponsive, mencoba reload.');
    setTimeout(() => {
      if (mainWindow && !mainWindow.isDestroyed()) mainWindow.reload();
    }, 1500);
  });
}

// ── IPC: Kirim hostname PC ke renderer ───────────────────────────
ipcMain.handle('get-pc-name', () => os.hostname());

// ── IPC: Token device untuk socket renderer ────────────────────────────────
ipcMain.handle('get-client-token', async () => {
  const cfg = loadServerConfig();
  return await ensureClientToken(cfg.serverUrl);
});

// ── IPC: Verifikasi emergency password (offline exit) ────────────────────────────────
// Password disimpan di main process supaya tidak ter-bundle di JS renderer.
// Bisa di-override via env LABKOM_EMERGENCY_PASSWORD saat build/install.
const EMERGENCY_PASSWORD = process.env.LABKOM_EMERGENCY_PASSWORD || null;
ipcMain.handle('verify-emergency-password', (_event, password) => {
  if (!EMERGENCY_PASSWORD || typeof password !== 'string' || !password) return false;
  // Constant-time compare biar tidak bocor via timing
  const a = Buffer.from(password);
  const b = Buffer.from(EMERGENCY_PASSWORD);
  return a.length === b.length && crypto.timingSafeEqual(a, b);
});

// ── IPC: Baca / Simpan konfigurasi URL server ────────────────────
ipcMain.handle('get-server-url', () => {
  const cfg = loadServerConfig();
  return cfg.serverUrl || null;
});
ipcMain.on('save-server-url', (_event, url) => {
  if (!isAllowedLabServerUrl(url)) return;
  const cfg = loadServerConfig();
  cfg.serverUrl = url;
  saveServerConfig(cfg);

  connectRealtime(url);
  startPresenceHeartbeat();

  if (screenShareState.active) {
    screenShareState.serverUrl = url;
    postScreenshot();
  }
});

function getPresencePayload() {
  const { mac, ip } = getFirstMac();
  return {
    pc_name: screenShareState.pcName,
    mac: mac || null,
    ip: ip || null,
    student_name: screenShareState.studentName || null,
    capabilities: {
      admin_screen_binary_v1: true,
      student_screen_binary_v1: true,
      multi_monitor_v1: true,
    },
    monitors: getMonitorMetadata(),
  };
}

async function connectRealtime(serverUrl) {
  if (!serverUrl) return;

  try {
    const nextOrigin = new URL(serverUrl).origin;
    if (realtimeSocket && realtimeSocket.io?.uri === nextOrigin) return;
    if (realtimeSocket) {
      realtimeSocket.removeAllListeners();
      realtimeSocket.disconnect();
      realtimeSocket = null;
    }

    const clientToken = await ensureClientToken(serverUrl);
    if (!clientToken) {
      logScreenWarning('[REALTIME] Tidak bisa register device ke server. Akan retry pada koneksi berikut.');
      return;
    }

    serverCapabilities = {
      student_screen_binary_v1: false,
      multi_monitor_v1: false,
    };
    realtimeSocket = io(nextOrigin, {
      transports: ['websocket', 'polling'],
      reconnection: true,
      timeout: 5000,
      auth: { role: 'client', client_token: clientToken },
    });

    realtimeSocket.on('connect', () => {
      const payload = getPresencePayload();
      realtimeSocket.emit('client:hello', payload);
      realtimeSocket.emit('client:heartbeat', payload);
      if (screenShareState.active) {
        postScreenshot();
      }
    });

    realtimeSocket.on('server:capabilities', (capabilities = {}) => {
      serverCapabilities = {
        student_screen_binary_v1: Boolean(capabilities.student_screen_binary_v1),
        multi_monitor_v1: Boolean(capabilities.multi_monitor_v1),
      };
      if (screenShareState.active) postScreenshot();
    });

    realtimeSocket.on('screen:quality', (payload = {}) => {
      const targetPcName = String(payload.pc_name || '').trim().toUpperCase();
      const currentPcName = String(screenShareState.pcName || '').trim().toUpperCase();
      if (targetPcName && currentPcName && targetPcName !== currentPcName) return;
      applyCaptureProfile(payload.mode || 'overview', payload.display_id || null);
    });

    realtimeSocket.on('disconnect', () => {
      applyCaptureProfile('overview');
      serverCapabilities.student_screen_binary_v1 = false;
      serverCapabilities.multi_monitor_v1 = false;
    });

    realtimeSocket.on('connect_error', async (err) => {
      applyCaptureProfile('overview');
      logScreenWarning(`[REALTIME] Gagal terhubung ke server realtime: ${err.message}`);
      // Jika unauthorized → token mungkin expired/revoked. Hapus & register ulang.
      if (String(err.message || '').toLowerCase().includes('unauthorized')) {
        log.warn('[DEVICE-AUTH] Token ditolak server, register ulang...');
        setStoredClientToken(null);
        const fresh = await requestDeviceToken(serverUrl);
        if (fresh && realtimeSocket) {
          realtimeSocket.auth = { role: 'client', client_token: fresh };
        }
      }
    });
  } catch (err) {
    logScreenWarning(`[REALTIME] Konfigurasi server realtime tidak valid: ${err.message}`);
  }
}

function disconnectRealtime() {
  if (!realtimeSocket) return;
  realtimeSocket.removeAllListeners();
  realtimeSocket.disconnect();
  realtimeSocket = null;
}

function startPresenceHeartbeat() {
  if (presenceHeartbeatTimer) return;

  const tick = () => {
    registerMacToServer();
    if (realtimeSocket?.connected) {
      realtimeSocket.emit('client:heartbeat', getPresencePayload());
    }
  };

  tick();
  presenceHeartbeatTimer = setInterval(tick, 10000);
}

function stopPresenceHeartbeat() {
  if (!presenceHeartbeatTimer) return;
  clearInterval(presenceHeartbeatTimer);
  presenceHeartbeatTimer = null;
}

// ── Screen sharing: capture & upload ke server ───────────────────────────
function postScreenshot() {
  if (!screenShareState.active || !screenShareState.serverUrl || screenCaptureInFlight) return;
  screenCaptureInFlight = true;
  const profile = getCaptureProfile();

  desktopCapturer.getSources({
    types: ['screen'],
    thumbnailSize: { width: profile.width, height: profile.height },
  }).then((sources) => {
    if (!sources.length) return;
    const monitors = getMonitorMetadata();
    const primaryDisplayId = String(screen.getPrimaryDisplay().id);
    const requestedDisplayId = captureDisplayId || primaryDisplayId;
    const selectedSource = sources.find((source) => String(source.display_id) === requestedDisplayId)
      || sources.find((source) => String(source.display_id) === primaryDisplayId)
      || sources[0];
    if (!selectedSource?.thumbnail || selectedSource.thumbnail.isEmpty()) return;

    const jpegBuf = selectedSource.thumbnail.toJPEG(profile.jpegQuality);
    const thumbnailSize = selectedSource.thumbnail.getSize();
    const displayId = String(selectedSource.display_id || requestedDisplayId);
    const monitor = monitors.find((item) => item.display_id === displayId);
    const binaryPayload = {
      frame: jpegBuf,
      mime: 'image/jpeg',
      width: thumbnailSize.width,
      height: thumbnailSize.height,
      sequence: ++screenCaptureSequence,
      captured_at: Date.now(),
      display_id: displayId,
      display_label: monitor?.label || selectedSource.name || 'Monitor',
      monitors,
      student_name: screenShareState.studentName || null,
    };

    if (realtimeSocket?.connected) {
      if (serverCapabilities.student_screen_binary_v1) {
        return new Promise((resolve) => {
          let settled = false;
          const finish = (ack = null) => {
            if (settled) return;
            settled = true;
            clearTimeout(timeout);
            if (ack && !ack.success) {
              logScreenWarning(`[SCREEN] Frame binary ditolak server: ${ack.message || ack.status || 'unknown'}`);
            }
            resolve();
          };
          const timeout = setTimeout(() => finish(), 1800);
          realtimeSocket.emit('client:screen-v2', binaryPayload, finish);
        });
      }

      realtimeSocket.emit('client:screen', {
        pc_name: screenShareState.pcName,
        student_name: screenShareState.studentName || null,
        image: `data:image/jpeg;base64,${jpegBuf.toString('base64')}`,
      });
      return;
    }

    try {
      const payload = {
        pc_name: screenShareState.pcName,
        student_name: screenShareState.studentName || null,
        image: `data:image/jpeg;base64,${jpegBuf.toString('base64')}`,
      };
      const body = JSON.stringify(payload);
      const parsed = new URL(`${screenShareState.serverUrl}/api/screens`);
      const headers = { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) };
      const tok = getStoredClientToken();
      if (tok) headers.Authorization = `Bearer ${tok}`;
      const req = http.request({
        hostname: parsed.hostname,
        port:     parseInt(parsed.port) || 3001,
        path:     '/api/screens',
        method:   'POST',
        headers,
      }, (res) => {
        res.resume();
      });
      req.on('error', (err) => {
        logScreenWarning(`[SCREEN] Gagal kirim screenshot: ${err.message}`);
      });
      req.setTimeout(3000, () => req.destroy());
      req.write(body);
      req.end();
    } catch (_) {}
  }).catch((err) => {
    logScreenWarning(`[SCREEN] Gagal capture layar: ${err.message}`);
  }).finally(() => {
    screenCaptureInFlight = false;
  });
}

function startScreenShare(serverUrl, studentName) {
  screenShareState.active = true;
  screenShareState.serverUrl = serverUrl;
  screenShareState.studentName = studentName || null;
  captureProfileMode = 'overview';
  captureDisplayId = null;
  screenCaptureSequence = 0;
  connectRealtime(serverUrl);

  restartScreenShareLoop();
  postScreenshot();
}

function stopScreenShare() {
  const activeServerUrl = screenShareState.serverUrl;
  screenShareState.active = false;
  screenShareState.studentName = null;
  screenShareState.serverUrl = null;

  if (screenShareTimer) {
    clearInterval(screenShareTimer);
    screenShareTimer = null;
  }
  screenCaptureInFlight = false;
  captureProfileMode = 'overview';
  captureDisplayId = null;

  if (realtimeSocket?.connected) {
    realtimeSocket.emit('client:screen-stop', {
      pc_name: screenShareState.pcName,
    });
    realtimeSocket.emit('client:heartbeat', getPresencePayload());
  }

  // Hapus screenshot dari server saat logout
  try {
    const resolvedServerUrl = activeServerUrl || loadServerConfig().serverUrl;
    if (!resolvedServerUrl) return;
    const parsed = new URL(`${resolvedServerUrl}/api/screens/${encodeURIComponent(screenShareState.pcName)}`);
    const headers = { 'Content-Type': 'application/json' };
    const tok = getStoredClientToken();
    if (tok) headers.Authorization = `Bearer ${tok}`;
    const req  = http.request({
      hostname: parsed.hostname, port: parseInt(parsed.port) || 3001,
      path:     parsed.pathname, method: 'DELETE',
      headers,
    }, () => {});
    req.on('error', () => {});
    req.end();
  } catch (_) {}
}

function postActivityToServer(serverUrl, activity) {
  if (!serverUrl || !activity) return;

  const body = JSON.stringify(activity);
  try {
    const parsed = new URL(`${serverUrl}/api/activities`);
    const headers = {
      'Content-Type': 'application/json',
      'Content-Length': Buffer.byteLength(body),
    };
    const tok = getStoredClientToken();
    if (tok) headers.Authorization = `Bearer ${tok}`;
    const req = http.request({
      hostname: parsed.hostname,
      port: parseInt(parsed.port) || 3001,
      path: '/api/activities',
      method: 'POST',
      headers,
    }, (res) => {
      res.resume();
    });
    req.on('error', (err) => {
      log.warn('[ACTIVITY] Gagal kirim activity via HTTP:', err.message);
    });
    req.setTimeout(5000, () => req.destroy());
    req.write(body);
    req.end();
  } catch (err) {
    log.warn('[ACTIVITY] URL server activity tidak valid:', err.message);
  }
}

function startActivityMonitoring(studentData = {}) {
  if (activityMonitor) {
    activityMonitor.stop();
    activityMonitor = null;
  }

  const cfg = loadServerConfig();
  activityMonitor = new ActivityMonitor();
  activityMonitor.setStudentInfo({
    pc_name: screenShareState.pcName,
    student_id: studentData.student_id || studentData.id || null,
    student_name: studentData.nama_lengkap || studentData.student_name || null,
    session_id: studentData.session_id || studentData.sessionId || null,
  });
  activityMonitor.onActivity((activity) => {
    if (realtimeSocket?.connected) {
      realtimeSocket.emit('client:activity', activity);
      return;
    }
    postActivityToServer(cfg.serverUrl, activity);
  });
  activityMonitor.start();
  log.info('[ACTIVITY] Monitoring dimulai untuk', studentData?.nama_lengkap);
}

// ── IPC: Login berhasil →’ keluar kiosk, tampilkan form pre-check ──
ipcMain.on('login-success', (_event, studentData) => {
  if (!mainWindow) return;
  activeSessionId = studentData?.session_id || studentData?.sessionId || null;

  applyWindowLayout('checklist');
  mainWindow.webContents.send('kiosk-off', studentData);

  // Mulai screen share
  const cfg = loadServerConfig();
  if (cfg.serverUrl) {
    startScreenShare(cfg.serverUrl, studentData?.nama_lengkap);
    startActivityMonitoring(studentData);
  }
});

// ── IPC: Resize widget dari React ────────────────────────────────
// mode: 'minimized' | 'regular' | 'expanded' | 'checklist'
ipcMain.on('resize-window', (_event, mode) => {
  if (!mainWindow) return;
  // Saat attention mode aktif, jangan ubah layout — simpan untuk restore nanti
  if (attentionModeOn) {
    preAttentionLayoutMode = mode;
    return;
  }
  applyWindowLayout(mode);
});

// ── IPC: Attention Mode dari server (via renderer) ────────────────────────────────
// Saat enabled: paksa kiosk fullscreen + keyboard hook + hide taskbar
// Saat disabled: kembalikan ke layout sebelumnya (widget/checklist)
ipcMain.on('set-attention-mode', (_event, enabled) => {
  if (!mainWindow || mainWindow.isDestroyed()) return;

  if (enabled && !attentionModeOn) {
    attentionModeOn = true;
    preAttentionLayoutMode = currentLayoutMode;
    applyWindowLayout('login');
    log.info('[ATTENTION] Aktif — paksa kiosk lock');
  } else if (!enabled && attentionModeOn) {
    attentionModeOn = false;
    const restoreMode = preAttentionLayoutMode || 'regular';
    preAttentionLayoutMode = null;
    applyWindowLayout(restoreMode);
    log.info('[ATTENTION] Nonaktif — restore layout:', restoreMode);
  }
});

// ── IPC: Logout →’ masuk kiosk lagi ───────────────────────────────
ipcMain.on('do-logout', () => {
  if (!mainWindow) return;
  activeSessionId = null;

  stopScreenShare(); // ← hentikan screen share

  // Stop Activity Monitoring
  if (activityMonitor) {
    activityMonitor.stop();
    activityMonitor = null;
    log.info('[ACTIVITY] Monitoring dihentikan');
  }

  applyWindowLayout('login');
  scheduleFocusRecovery(50);
  mainWindow.webContents.send('return-to-login');
});

// ── IPC: Keluar aplikasi (setelah password kepala lab terverifikasi) ──
ipcMain.on('quit-app', () => {
  requestControlledQuit('admin-verified-exit');
});

ipcMain.on('admin-exit-dialog-closed', () => {
  const restoreMode = preAdminExitLayoutMode;
  preAdminExitLayoutMode = null;
  if (restoreMode && !attentionModeOn) applyWindowLayout(restoreMode);
});

// ── IPC: Verify server dari main process (bypass renderer fetch restriction) ──
ipcMain.handle('verify-server', async (_event, url) => {
  if (!isAllowedLabServerUrl(url)) return { ok: false, labkom: false };
  return new Promise((resolve) => {
    const parsed = new URL(url);
    const req = http.request(
      { host: parsed.hostname, port: parseInt(parsed.port) || 3001, path: '/', method: 'GET' },
      (res) => {
        let body = '';
        res.on('data', d => body += d);
        res.on('end', () => {
          try {
            const json = JSON.parse(body);
            resolve({ ok: res.statusCode < 400, labkom: json.message?.includes('Labkom') });
          } catch { resolve({ ok: res.statusCode < 400, labkom: false }); }
        });
      }
    );
    req.setTimeout(4000, () => { req.destroy(); resolve({ ok: false, labkom: false }); });
    req.on('error', () => resolve({ ok: false, labkom: false }));
    req.end();
  });
});

// ── IPC: Keluar dari setup screen (belum login, aman untuk keluar) ──
ipcMain.on('exit-app', () => {
  requestControlledQuit('setup-exit');
});

// ═══ Remote Power Control ═════════════════════════════════════════════════

// ── Helper MAC address ────────────────────────────────────────────────────
function getFirstMac() {
  const ifaces = os.networkInterfaces();
  for (const name of Object.keys(ifaces)) {
    for (const iface of ifaces[name]) {
      if (iface.family === 'IPv4' && !iface.internal && iface.mac && iface.mac !== '00:00:00:00:00:00') {
        return { mac: iface.mac, ip: iface.address };
      }
    }
  }
  return { mac: null, ip: null };
}

// ── Daftarkan MAC ke server ───────────────────────────────────────────────
function registerMacToServer() {
  const cfg = loadServerConfig();
  if (!cfg.serverUrl) return;
  const { mac, ip } = getFirstMac();
  if (!mac) return;
  const body = JSON.stringify({
    pc_name: screenShareState.pcName,
    mac,
    ip,
    student_name: screenShareState.studentName || null,
  });
  try {
    const parsed = new URL(`${cfg.serverUrl}/api/client-cmd/register-mac`);
    const req = http.request({
      hostname: parsed.hostname, port: parseInt(parsed.port) || 3001,
      path: '/api/client-cmd/register-mac', method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(body),
        Authorization: `Bearer ${getStoredClientToken() || ''}`,
      },
    }, () => {});
    req.on('error', () => {}); req.setTimeout(4000, () => req.destroy()); req.write(body); req.end();
  } catch (_) {}
}

// ── Install watchdog via Windows Task Scheduler ───────────────────────────
function installWatchdog() {
  if (isDev) return;
  try {
    const exePath  = process.execPath;
    const userData = app.getPath('userData');
    const flagPath = path.join(userData, 'disabled.flag');
    const cfgPath  = path.join(userData, 'server.config.json');
    const devicePath = getDevicePath();
    const ps1Path  = path.join(userData, 'labkom-watchdog.ps1');
    const exeEsc   = exePath.replace(/'/g, "''");

    const ps1Lines = [
      `$appPath  = '${exeEsc}'`,
      `$flagPath = '${flagPath.replace(/'/g, "''")}'`,
      `$cfgPath  = '${cfgPath.replace(/'/g, "''")}'`,
      `$devicePath = '${devicePath.replace(/'/g, "''")}'`,
      '',
      `$isRunning = (Get-Process -Name 'LabKom Siswa' -ErrorAction SilentlyContinue) -ne $null`,
      `if ($isRunning) { exit 0 }`,
      '',
      `$serverUrl = $null`,
      `$clientToken = $null`,
      `if (Test-Path $cfgPath) {`,
      `  try { $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json; $serverUrl = $cfg.serverUrl } catch {}`,
      `}`,
      `if (Test-Path $devicePath) {`,
      `  try { $device = Get-Content $devicePath -Raw | ConvertFrom-Json; $clientToken = $device.client_token } catch {}`,
      `}`,
      '',
      `$cmd = 'none'`,
      `if ($serverUrl) {`,
      `  try {`,
      `    $headers = @{ Authorization = "Bearer $clientToken" }`,
      `    $r = Invoke-WebRequest -Uri "$serverUrl/api/client-cmd/current" -Headers $headers -TimeoutSec 4 -UseBasicParsing`,
      `    $cmd = ($r.Content | ConvertFrom-Json).cmd`,
      `  } catch {}`,
      `}`,
      '',
      `if ($cmd -eq 'enable') {`,
      `  if (Test-Path $flagPath) { Remove-Item $flagPath -Force }`,
      `  Enable-ScheduledTask -TaskName 'LabKomWatchdog' -ErrorAction SilentlyContinue`,
      `  if (Test-Path $appPath) { Start-Process $appPath }`,
      `  exit 0`,
      `}`,
      '',
      `if ((-not (Test-Path $flagPath)) -and (Test-Path $appPath)) { Start-Process $appPath }`,
    ];
    fs.writeFileSync(ps1Path, ps1Lines.join('\r\n'), 'utf-8');

    const q      = ps1Path.replace(/"/g, '\\"');
    const runner = `powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "${q}"`;
    execSync(`schtasks /Create /TN "LabKomWatchdog" /TR "${runner.replace(/"/g, '\\"')}" /SC MINUTE /MO 2 /F /RL HIGHEST`, { timeout: 10000 });
    log.info('[WATCHDOG] Task Scheduler terdaftar.');
  } catch (err) {
    log.warn('[WATCHDOG] Gagal install watchdog:', err.message);
  }
}

// ── Polling perintah remote dari server ──────────────────────────────────
let cmdPollTimer = null;
function startCmdPolling() {
  if (cmdPollTimer) return;
  cmdPollTimer = setInterval(() => {
    const cfg = loadServerConfig();
    if (!cfg.serverUrl) return;
    try {
      const parsed = new URL(`${cfg.serverUrl}/api/client-cmd/current`);
      const req = http.request({
        hostname: parsed.hostname, port: parseInt(parsed.port) || 3001,
        path: '/api/client-cmd/current', method: 'GET',
        headers: { Authorization: `Bearer ${getStoredClientToken() || ''}` },
      }, (res) => {
        let body = '';
        res.on('data', d => body += d);
        res.on('end', () => {
          try {
            const json = JSON.parse(body);
            if (json.cmd === 'kill') {
              log.info('[CMD] Perintah kill diterima  menutup aplikasi');
              stopScreenShare();
              if (json.permanent) {
                const fp = path.join(app.getPath('userData'), 'disabled.flag');
                fs.writeFileSync(fp, new Date().toISOString(), 'utf-8');
                try { execSync('schtasks /Change /TN "LabKomWatchdog" /Disable', { timeout: 4000 }); } catch {}
              }
              requestControlledQuit(json.permanent ? 'remote-kill-permanent' : 'remote-kill');
            } else if (json.cmd === 'enable') {
              const fp = path.join(app.getPath('userData'), 'disabled.flag');
              if (fs.existsSync(fp)) {
                try { fs.unlinkSync(fp); } catch {}
                try { execSync('schtasks /Change /TN "LabKomWatchdog" /Enable', { timeout: 4000 }); } catch {}
              }
            }
          } catch {}
        });
      });
      req.setTimeout(5000, () => req.destroy());
      req.on('error', () => {});
      req.end();
    } catch (_) {}
  }, 10_000);
}

// ── Auto force-logout ke server saat app mau ditutup ─────────────
function logoutActiveSessionOnQuit() {
  const cfg = loadServerConfig();
  const token = getStoredClientToken();
  if (!cfg.serverUrl || !activeSessionId || !token) return;
  try {
    const parsed = new URL(`${cfg.serverUrl}/api/auth/logout`);
    const body = JSON.stringify({ session_id: activeSessionId });
    const req = http.request({
      host: parsed.hostname,
      port: parseInt(parsed.port) || 3001,
      path: '/api/auth/logout',
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(body),
        Authorization: `Bearer ${token}`,
      },
    }, () => {});
    req.on('error', () => {});
    req.write(body);
    req.end();
  } catch (_) {}
}
// ── IPC: Request HTTP renderer, dibatasi ke backend LabKom tersimpan ──────
function isAllowedRendererApiUrl(parsed) {
  if (!isAllowedLabServerUrl(parsed.origin)) return false;
  if (!parsed.pathname.startsWith('/api/')) return false;
  const configuredUrl = loadServerConfig().serverUrl;
  if (!configuredUrl) return false;
  try {
    return parsed.origin === new URL(configuredUrl).origin;
  } catch {
    return false;
  }
}

function performRendererApiRequest(parsed, options, clientToken) {
  return new Promise((resolve) => {
    const bodyStr = typeof options.body === 'string' ? options.body : '';
    if (Buffer.byteLength(bodyStr) > 2 * 1024 * 1024) {
      return resolve({ ok: false, status: 413, data: { success: false, message: 'Payload terlalu besar.' } });
    }

    const method = String(options.method || 'GET').toUpperCase();
    if (!['GET', 'POST', 'PUT', 'DELETE'].includes(method)) {
      return resolve({ ok: false, status: 405, data: { success: false, message: 'Method tidak diizinkan.' } });
    }

    const headers = { 'Content-Type': 'application/json' };
    if (clientToken) headers.Authorization = `Bearer ${clientToken}`;
    if (bodyStr) headers['Content-Length'] = Buffer.byteLength(bodyStr);

    const req = http.request({
      hostname: parsed.hostname,
      port: parseInt(parsed.port) || 3001,
      path: parsed.pathname + (parsed.search || ''),
      method,
      headers,
    }, (res) => {
      let body = '';
      let bytes = 0;
      res.on('data', (chunk) => {
        bytes += chunk.length;
        if (bytes > 2 * 1024 * 1024) {
          req.destroy();
          resolve({ ok: false, status: 502, data: { success: false, message: 'Respons server terlalu besar.' } });
          return;
        }
        body += chunk;
      });
      res.on('end', () => {
        try { resolve({ ok: res.statusCode < 400, status: res.statusCode, data: JSON.parse(body) }); }
        catch { resolve({ ok: res.statusCode < 400, status: res.statusCode, data: body }); }
      });
    });
    req.setTimeout(8000, () => { req.destroy(); resolve({ ok: false, status: 0, data: null }); });
    req.on('error', () => resolve({ ok: false, status: 0, data: null }));
    if (bodyStr) req.write(bodyStr);
    req.end();
  });
}

ipcMain.handle('api-request', async (_event, url, options = {}) => {
  let parsed;
  try { parsed = new URL(url); }
  catch { return { ok: false, status: 400, data: { success: false, message: 'URL tidak valid.' } }; }

  if (!isAllowedRendererApiUrl(parsed)) {
    return { ok: false, status: 403, data: { success: false, message: 'Target API tidak diizinkan.' } };
  }

  let result = await performRendererApiRequest(parsed, options, getStoredClientToken());
  if (result.status === 401) {
    setStoredClientToken(null);
    const freshToken = await requestDeviceToken(parsed.origin);
    if (freshToken) result = await performRendererApiRequest(parsed, options, freshToken);
  }
  return result;

});

// Verifikasi password keluar admin selalu memakai server yang tersimpan di main process.
// Renderer hanya mengirim password dan tidak dapat memilih/memalsukan target API.
ipcMain.handle('verify-admin-exit-password', async (_event, password) => {
  if (typeof password !== 'string' || !password || password.length > 256) {
    return { ok: false, status: 400, data: { success: false, message: 'Password tidak valid.' } };
  }

  const configuredUrl = loadServerConfig().serverUrl;
  if (!configuredUrl) {
    return { ok: false, status: 503, data: { success: false, message: 'Server Admin belum dikonfigurasi.' } };
  }

  let parsed;
  try {
    parsed = new URL('/api/admin/verify-password', `${configuredUrl.replace(/\/$/, '')}/`);
  } catch {
    return { ok: false, status: 503, data: { success: false, message: 'Alamat Server Admin tidak valid.' } };
  }

  if (!isAllowedRendererApiUrl(parsed)) {
    return { ok: false, status: 403, data: { success: false, message: 'Server Admin tersimpan tidak diizinkan.' } };
  }

  return performRendererApiRequest(parsed, {
    method: 'POST',
    body: JSON.stringify({ password }),
  }, null);
});
app.whenReady().then(() => {
  // ── Daftarkan ke Windows Startup ─────────────────────────────
  // Agar app otomatis berjalan saat PC dinyalakan (kiosk mode)
  if (!isDev) {
    app.setLoginItemSettings({
      openAtLogin: true,
      openAsHidden: false,
      name: 'LabKom Siswa',
    });
  }

  createWindow();
  applyWindowLayout('login');
  startDiscoveryListener(); // → Dengarkan broadcast admin

  const initialConfig = loadServerConfig();
  if (initialConfig.serverUrl) connectRealtime(initialConfig.serverUrl);
  startPresenceHeartbeat();

  // Daftarkan MAC + mulai polling perintah remote setelah app siap
  setTimeout(() => {
    registerMacToServer();
    startCmdPolling();
  }, 8000);

  // Install watchdog Task Scheduler (hanya production)
  if (!isDev) installWatchdog();

  // Silent update check 30 detik setelah startup (hanya production)
  if (!isDev) {
    setTimeout(() => {
      autoUpdater.checkForUpdates().catch(() => {});
    }, 30_000);
  }

  // ── Shortcut keluar untuk Kepala Lab (Ctrl+Alt+Q) ───────────────
  globalShortcut.register('Ctrl+Alt+Q', () => {
    if (mainWindow && !mainWindow.isDestroyed()) {
      if (!attentionModeOn && currentLayoutMode !== 'login' && !preAdminExitLayoutMode) {
        preAdminExitLayoutMode = currentLayoutMode;
      }
      if (currentLayoutMode !== 'login') applyWindowLayout('login');
      mainWindow.webContents.send('show-admin-dialog');
    }
  });

  // ── Blokir shortcut berbahaya ─────────────────────────────────
  // Alt+F4, Ctrl+W, Ctrl+F4 (close window)
  globalShortcut.register('Alt+F4',  () => {});
  globalShortcut.register('Ctrl+W',  () => {});
  globalShortcut.register('Ctrl+F4', () => {});
  // Task Manager & Alt+Tab
  globalShortcut.register('Ctrl+Shift+Escape', () => {});
  globalShortcut.register('Ctrl+Alt+Delete',   () => {});
  globalShortcut.register('Ctrl+Esc',          () => {});
  globalShortcut.register('Alt+Esc',           () => {});
  globalShortcut.register('Alt+Tab',            () => {});
  globalShortcut.register('Alt+Space',         () => {});
  globalShortcut.register('Meta+Tab',           () => {});
  globalShortcut.register('Meta+D',             () => {});
  // F11, F4
  globalShortcut.register('F11', () => {});
  globalShortcut.register('F4',  () => {});
  // DevTools (nonaktif di production)
  if (!isDev) {
    globalShortcut.register('F12',            () => {});
    globalShortcut.register('Ctrl+Shift+I',   () => {});
    globalShortcut.register('Ctrl+Shift+J',   () => {});
    globalShortcut.register('Ctrl+R',         () => {});
    globalShortcut.register('F5',             () => {});
  }
});

// Jangan tutup app saat semua window ditutup
app.on('window-all-closed', (e) => e.preventDefault());
app.on('browser-window-blur', (_event, window) => {
  if (window === mainWindow) {
    if (isKioskLocked()) {
      scheduleFocusRecovery(10);
    } else {
      scheduleFocusRecovery(150);
    }
  }
});
app.on('browser-window-focus', (_event, window) => {
  if (window !== mainWindow) return;
  if (focusRecoveryTimer) {
    clearTimeout(focusRecoveryTimer);
    focusRecoveryTimer = null;
  }
});
app.on('before-quit', (event) => {
  if (!allowAppQuit) {
    preventUnexpectedQuit(event);
    return;
  }
  logoutActiveSessionOnQuit();
});
app.on('second-instance', () => {
  if (!mainWindow || mainWindow.isDestroyed()) return;
  if (mainWindow.isMinimized()) mainWindow.restore();
  keepWindowVisible();
});

app.on('will-quit', () => {
  stopDiscoveryListener();
  stopPresenceHeartbeat();
  disconnectRealtime();
  stopKeyboardHook();
  showTaskbar(); // Safety net: selalu pulihkan taskbar
  if (cmdPollTimer) { clearInterval(cmdPollTimer); cmdPollTimer = null; }

  // Cleanup Activity Monitor
  if (activityMonitor) {
    activityMonitor.stop();
    activityMonitor = null;
  }

  globalShortcut.unregisterAll();
});
