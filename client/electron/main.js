const { app, BrowserWindow, globalShortcut, ipcMain, screen, desktopCapturer } = require('electron');
const os              = require('os');
const path            = require('path');
const fs              = require('fs');
const http            = require('http');
const dgram           = require('dgram');
const { execSync, spawn } = require('child_process');
const { io }          = require('socket.io-client');
const ActivityMonitor = require('./activityMonitor');

// â”€â”€ Chromium flags: izinkan fetch dari file:// ke http:// LAN â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
app.commandLine.appendSwitch('disable-web-security');
app.commandLine.appendSwitch('allow-running-insecure-content');
app.commandLine.appendSwitch('allow-insecure-localhost');

// â”€â”€ Single instance: hanya aktif di production supaya dev client bisa jalan berdampingan
if (app.isPackaged && !app.requestSingleInstanceLock()) {
  app.quit();
}

// â”€â”€â”€ UDP Discovery Listener â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

// â”€â”€ Auto-Updater (silent background update) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

// â”€â”€ Path file konfigurasi server URL â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
function getConfigPath() {
  return path.join(app.getPath('userData'), 'server.config.json');
}
function loadServerConfig() {
  try {
    const raw = fs.readFileSync(getConfigPath(), 'utf-8');
    return JSON.parse(raw);
  } catch { return {}; }
}
function saveServerConfig(data) {
  fs.writeFileSync(getConfigPath(), JSON.stringify(data, null, 2), 'utf-8');
}

let mainWindow;
let focusRecoveryTimer = null;
let aggressiveFocusInterval = null;
let screenShareTimer   = null;
let screenCaptureInFlight = false;

// ── Windows Keyboard Hook (blokir Alt+Tab di level OS) ──────────────────────
let kbHookProcess = null;
let kbHookFlagPath = null;

function startKeyboardHook() {
  if (process.platform !== 'win32') return;
  if (kbHookProcess) return; // Sudah berjalan

  try {
    // Tentukan path script PS1
    const ps1Path = app.isPackaged
      ? path.join(path.dirname(process.execPath), 'resources', 'electron', 'blockAltTab.ps1')
      : path.join(__dirname, 'blockAltTab.ps1');

    if (!fs.existsSync(ps1Path)) {
      log.warn('[KIOSK] blockAltTab.ps1 tidak ditemukan di:', ps1Path);
      return;
    }

    // File flag untuk menghentikan hook
    kbHookFlagPath = path.join(app.getPath('userData'), 'kbhook-stop.flag');
    // Hapus flag lama kalau ada
    try { if (fs.existsSync(kbHookFlagPath)) fs.unlinkSync(kbHookFlagPath); } catch {}

    // Dapatkan PID Electron untuk dikirim ke PS1 agar bisa force-focus window
    const electronPID = process.pid;

    kbHookProcess = spawn('powershell.exe', [
      '-NoProfile',
      '-NonInteractive',
      '-WindowStyle', 'Hidden',
      '-ExecutionPolicy', 'Bypass',
      '-File', ps1Path,
      '-FlagFile', kbHookFlagPath,
      '-ElectronPID', String(electronPID),
    ], {
      detached: false,
      stdio: ['ignore', 'pipe', 'pipe'],
    });

    kbHookProcess.stdout?.on('data', (d) => log.info('[KBHOOK stdout]', d.toString().trim()));
    kbHookProcess.stderr?.on('data', (d) => log.warn('[KBHOOK stderr]', d.toString().trim()));

    kbHookProcess.on('exit', (code) => {
      log.info('[KIOSK] Keyboard hook process keluar, kode:', code);
      kbHookProcess = null;
      // Otomatis restart jika masih dalam mode lock
      if (isKioskLocked()) {
        log.info('[KIOSK] Keyboard hook keluar saat masih lock, restart...');
        setTimeout(() => startKeyboardHook(), 500);
      }
    });

    kbHookProcess.on('error', (err) => {
      log.warn('[KIOSK] Gagal menjalankan keyboard hook:', err.message);
      kbHookProcess = null;
    });

    log.info('[KIOSK] Windows keyboard hook dimulai (PID:', kbHookProcess.pid, '), script:', ps1Path);
  } catch (err) {
    log.warn('[KIOSK] Error saat memulai keyboard hook:', err.message);
    kbHookProcess = null;
  }
}

function stopKeyboardHook() {
  if (process.platform !== 'win32') return;

  // Kirim sinyal stop via flag file
  if (kbHookFlagPath) {
    try { fs.writeFileSync(kbHookFlagPath, 'stop', 'utf-8'); } catch {}
  }

  // Kill proses jika masih berjalan setelah 1 detik
  if (kbHookProcess) {
    setTimeout(() => {
      if (kbHookProcess) {
        try { kbHookProcess.kill(); } catch {}
        kbHookProcess = null;
      }
    }, 1000);
  }

  log.info('[KIOSK] Windows keyboard hook dihentikan');
}

// ── Windows Taskbar Hide/Show (sembunyikan taskbar saat lock) ──────────────
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

// â"€â"€ Activity Monitor Instance â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
let activityMonitor = null;
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
    intervalMs: 450,
  },
};
let captureProfileMode = 'overview';

// â”€â”€ Ukuran widget per mode â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
  return Boolean(mainWindow && !mainWindow.isDestroyed() && mainWindow.isKiosk() && mainWindow.isFullScreen());
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

function applyWindowLayout(mode = 'regular') {
  if (!mainWindow || mainWindow.isDestroyed()) return;

  const isLoginLayout = mode === 'login';
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

function applyCaptureProfile(mode = 'overview') {
  const nextMode = CAPTURE_PROFILES[mode] ? mode : 'overview';
  if (captureProfileMode === nextMode) return;

  captureProfileMode = nextMode;
  log.info(`[SCREEN] Capture profile -> ${nextMode}`);
  restartScreenShareLoop();

  if (screenShareState.active) {
    postScreenshot();
  }
}

function createWindow() {
  mainWindow = new BrowserWindow({
    // â”€â”€ Kiosk & tampilan â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    kiosk:          true,   // Full screen mutlak, tutupi taskbar
    fullscreen:     true,
    alwaysOnTop:    true,
    frame:          false,  // Hapus title bar / window border
    transparent:    true,   // Background OS transparan â†’ widget melayang
    skipTaskbar:    true,   // Sembunyikan dari taskbar Windows
    movable:        false,
    autoHideMenuBar:true,
    hasShadow:      true,

    // â”€â”€ Nonaktifkan close akibat tombol â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    closable:       false,
    minimizable:    false,
    maximizable:    false,
    resizable:      false,

    // â”€â”€ Keamanan Electron â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    webPreferences: {
      preload:           path.join(__dirname, 'preload.js'),
      contextIsolation:  true,
      nodeIntegration:   false,
      devTools:          allowDevTools,
      spellcheck:        false,
      webSecurity:       false,  // izinkan fetch dari file:// ke http://
    },
  });
  mainWindow.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true });
  mainWindow.setMenuBarVisibility(false);
  mainWindow.removeMenu();

  // â”€â”€ Load URL â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  if (isDev) {
    mainWindow.loadURL('http://localhost:5173');
    if (allowDevTools) {
      mainWindow.webContents.openDevTools({ mode: 'detach' });
    }
  } else {
    mainWindow.loadFile(path.join(__dirname, '../dist/index.html'));
  }

  // â”€â”€ Cegah navigasi keluar â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  mainWindow.webContents.on('will-navigate', (e, url) => {
    if (!url.startsWith('http://localhost:5173') && !url.startsWith('file://')) {
      e.preventDefault();
    }
  });

  // â”€â”€ Cegah buka jendela baru â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  mainWindow.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));

  // ── Blokir Alt+F4 dan shortcut berbahaya di level webContents ─────────
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

  // Electron tidak bisa benar-benar memblokir Alt+Tab di Windows.
  // Yang bisa kita lakukan adalah memulihkan fokus kiosk secepat mungkin.
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

// â”€â”€ IPC: Kirim hostname PC ke renderer â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
ipcMain.handle('get-pc-name', () => os.hostname());

// â”€â”€ IPC: Baca / Simpan konfigurasi URL server â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
ipcMain.handle('get-server-url', () => {
  const cfg = loadServerConfig();
  return cfg.serverUrl || null;
});
ipcMain.on('save-server-url', (_event, url) => {
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
  };
}

function connectRealtime(serverUrl) {
  if (!serverUrl) return;

  try {
    const nextOrigin = new URL(serverUrl).origin;
    if (realtimeSocket && realtimeSocket.io?.uri === nextOrigin) return;
    if (realtimeSocket) {
      realtimeSocket.removeAllListeners();
      realtimeSocket.disconnect();
      realtimeSocket = null;
    }

    realtimeSocket = io(nextOrigin, {
      transports: ['websocket', 'polling'],
      reconnection: true,
      timeout: 5000,
      auth: { role: 'client' },
    });

    realtimeSocket.on('connect', () => {
      const payload = getPresencePayload();
      realtimeSocket.emit('client:hello', payload);
      realtimeSocket.emit('client:heartbeat', payload);
      if (screenShareState.active) {
        postScreenshot();
      }
    });

    realtimeSocket.on('screen:quality', (payload = {}) => {
      const targetPcName = String(payload.pc_name || '').trim().toUpperCase();
      const currentPcName = String(screenShareState.pcName || '').trim().toUpperCase();
      if (targetPcName && currentPcName && targetPcName !== currentPcName) return;
      applyCaptureProfile(payload.mode || 'overview');
    });

    realtimeSocket.on('disconnect', () => {
      applyCaptureProfile('overview');
    });

    realtimeSocket.on('connect_error', (err) => {
      applyCaptureProfile('overview');
      logScreenWarning(`[REALTIME] Gagal terhubung ke server realtime: ${err.message}`);
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

// â”€â”€ Screen sharing: capture & upload ke server â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
function postScreenshot() {
  if (!screenShareState.active || !screenShareState.serverUrl || screenCaptureInFlight) return;
  screenCaptureInFlight = true;
  const profile = getCaptureProfile();

  desktopCapturer.getSources({
    types: ['screen'],
    thumbnailSize: { width: profile.width, height: profile.height },
  }).then((sources) => {
    if (!sources.length) return;
    const primaryDisplayId = String(screen.getPrimaryDisplay().id);
    const selectedSource = sources.find((source) => String(source.display_id) === primaryDisplayId) || sources[0];
    if (!selectedSource?.thumbnail || selectedSource.thumbnail.isEmpty()) return;

    const jpegBuf = selectedSource.thumbnail.toJPEG(profile.jpegQuality);
    const b64     = jpegBuf.toString('base64');
    const payload = {
      pc_name:      screenShareState.pcName,
      student_name: screenShareState.studentName || null,
      image:        `data:image/jpeg;base64,${b64}`,
    };
    const body    = JSON.stringify(payload);

    if (realtimeSocket?.connected) {
      realtimeSocket.emit('client:screen', payload);
      return;
    }

    try {
      const parsed = new URL(`${screenShareState.serverUrl}/api/screens`);
      const req    = http.request({
        hostname: parsed.hostname,
        port:     parseInt(parsed.port) || 3001,
        path:     '/api/screens',
        method:   'POST',
        headers:  { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) },
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
    const req  = http.request({
      hostname: parsed.hostname, port: parseInt(parsed.port) || 3001,
      path:     parsed.pathname, method: 'DELETE',
      headers:  { 'Content-Type': 'application/json' },
    }, () => {});
    req.on('error', () => {});
    req.end();
  } catch (_) {}
}

// â”€â”€ IPC: Login berhasil â†’ keluar kiosk, tampilkan form pre-check â”€â”€
ipcMain.on('login-success', (_event, studentData) => {
  if (!mainWindow) return;

  applyWindowLayout('checklist');
  mainWindow.webContents.send('kiosk-off', studentData);

  // Mulai screen share
  const cfg = loadServerConfig();
  if (cfg.serverUrl) {
    startScreenShare(cfg.serverUrl, studentData?.nama_lengkap);
    
    // Start Activity Monitoring
    if (activityMonitor) {
      activityMonitor.stop();
      activityMonitor = null;
    }
    
    activityMonitor = new ActivityMonitor({
      socket: realtimeSocket,
      studentId: studentData?.id,
      studentName: studentData?.nama_lengkap,
      pcName: screenShareState.pcName,
      sessionId: studentData?.sessionId || null,
    });
    
    activityMonitor.start();
    log.info('[ACTIVITY] Monitoring dimulai untuk', studentData?.nama_lengkap);
  }
});

// â”€â”€ IPC: Resize widget dari React â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// mode: 'minimized' | 'regular' | 'expanded' | 'checklist'
ipcMain.on('resize-window', (_event, mode) => {
  if (!mainWindow) return;
  applyWindowLayout(mode);
});

// â”€â”€ IPC: Logout â†’ masuk kiosk lagi â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
ipcMain.on('do-logout', () => {
  if (!mainWindow) return;

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

// â”€â”€ IPC: Keluar aplikasi (setelah password kepala lab terverifikasi) â”€â”€
ipcMain.on('quit-app', () => {
  requestControlledQuit('admin-verified-exit');
});

// â”€â”€ IPC: Verify server dari main process (bypass renderer fetch restriction) â”€â”€
ipcMain.handle('verify-server', async (_event, url) => {
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

// â”€â”€ IPC: Keluar dari setup screen (belum login, aman untuk keluar) â”€â”€
ipcMain.on('exit-app', () => {
  requestControlledQuit('setup-exit');
});

// â•â•â• Remote Power Control â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

// â”€â”€ Helper MAC address â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

// â”€â”€ Daftarkan MAC ke server â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
      headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) },
    }, () => {});
    req.on('error', () => {}); req.setTimeout(4000, () => req.destroy()); req.write(body); req.end();
  } catch (_) {}
}

// â”€â”€ Install watchdog via Windows Task Scheduler â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
function installWatchdog() {
  if (isDev) return;
  try {
    const exePath  = process.execPath;
    const userData = app.getPath('userData');
    const flagPath = path.join(userData, 'disabled.flag');
    const cfgPath  = path.join(userData, 'server.config.json');
    const ps1Path  = path.join(userData, 'labkom-watchdog.ps1');
    const exeEsc   = exePath.replace(/'/g, "''");

    const ps1Lines = [
      `$appPath  = '${exeEsc}'`,
      `$flagPath = '${flagPath.replace(/'/g, "''")}'`,
      `$cfgPath  = '${cfgPath.replace(/'/g, "''")}'`,
      '',
      `$isRunning = (Get-Process -Name 'LabKom Siswa' -ErrorAction SilentlyContinue) -ne $null`,
      `if ($isRunning) { exit 0 }`,
      '',
      `$serverUrl = $null`,
      `if (Test-Path $cfgPath) {`,
      `  try { $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json; $serverUrl = $cfg.serverUrl } catch {}`,
      `}`,
      '',
      `$cmd = 'none'`,
      `if ($serverUrl) {`,
      `  try {`,
      `    $r = Invoke-WebRequest -Uri "$serverUrl/api/client-cmd/current" -TimeoutSec 4 -UseBasicParsing`,
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

// â”€â”€ Polling perintah remote dari server â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
      }, (res) => {
        let body = '';
        res.on('data', d => body += d);
        res.on('end', () => {
          try {
            const json = JSON.parse(body);
            if (json.cmd === 'kill') {
              log.info('[CMD] Perintah kill diterima â€“ menutup aplikasi');
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

// â”€â”€ Auto force-logout ke server saat app mau ditutup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
function forceLogoutOnQuit() {
  const pcName = os.hostname();
  const cfg = loadServerConfig();
  if (!cfg.serverUrl) return;
  // Gunakan Node.js http langsung untuk cleanup sesi saat app ditutup
  try {
    const parsed = new URL(`${cfg.serverUrl}/api/auth/force-logout`);
    const body = JSON.stringify({ pc_name: pcName });
    const req = http.request(
      { host: parsed.hostname, port: parseInt(parsed.port) || 3001, path: '/api/auth/force-logout', method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) } },
      () => {}
    );
    req.on('error', () => {});
    req.write(body);
    req.end();
  } catch (_) {}
}
// â”€â”€ IPC: Request HTTP umum dari renderer (Node.js http, bypass Chromium) â”€â”€â”€â”€
ipcMain.handle('api-request', (_event, url, options = {}) => {
  return new Promise((resolve) => {
    let parsed;
    try { parsed = new URL(url); } catch { return resolve({ ok: false, status: 0, data: null }); }
    const bodyStr = options.body || '';
    const reqOpts = {
      hostname: parsed.hostname,
      port:     parseInt(parsed.port) || 3001,
      path:     parsed.pathname + (parsed.search || ''),
      method:   (options.method || 'GET').toUpperCase(),
      headers:  { 'Content-Type': 'application/json', ...(options.headers || {}) },
    };
    if (bodyStr) reqOpts.headers['Content-Length'] = Buffer.byteLength(bodyStr);
    const req = http.request(reqOpts, (res) => {
      let body = '';
      res.on('data', (d) => body += d);
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
});

app.whenReady().then(() => {
  // â”€â”€ Daftarkan ke Windows Startup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
  startDiscoveryListener(); // â† Dengarkan broadcast admin

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

  // â”€â”€ Shortcut keluar untuk Kepala Lab (Ctrl+Alt+Q) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  globalShortcut.register('Ctrl+Alt+Q', () => {
    if (mainWindow && !mainWindow.isDestroyed()) {
      mainWindow.webContents.send('show-admin-dialog');
    }
  });

  // â”€â”€ Blokir shortcut berbahaya â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
  forceLogoutOnQuit();
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
