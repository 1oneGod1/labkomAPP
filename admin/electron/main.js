// ─── Admin Electron Main Process ───────────────────────────────────────────
// Tugasnya:
//  1. Spawn backend Express server secara otomatis
//  2. Tampilkan UI admin dashboard (React/Vite)
//  3. Expose info IP LAN ke renderer via IPC
//  4. Auto-update via electron-updater (GitHub Releases / generic server)

const { app, BrowserWindow, ipcMain, shell, dialog, globalShortcut } = require('electron');
const path             = require('path');
const os               = require('os');
const http             = require('http');
const dgram            = require('dgram');
const { spawn }        = require('child_process');
const fs               = require('fs');

// ─── UDP Discovery Broadcast ────────────────────────────────────────────────
const DISCOVERY_PORT    = 41234;
const DISCOVERY_MESSAGE = Buffer.from(JSON.stringify({
  labkom: true,
  productName: 'LabKom Admin',
}));
let discoverySocket = null;
let discoveryTimer  = null;

function startDiscoveryBroadcast() {
  if (discoverySocket) return;
  discoverySocket = dgram.createSocket({ type: 'udp4', reuseAddr: true });
  discoverySocket.bind(() => {
    discoverySocket.setBroadcast(true);
    // Kirim broadcast tiap 2 detik untuk SEMUA IP LAN
    discoveryTimer = setInterval(() => {
      const ips  = getAllLanIps();
      const port = serverPort;
      ips.forEach(ip => {
        const msg = Buffer.from(JSON.stringify({ labkom: true, ip, port, name: 'LabKom Admin' }));
        discoverySocket.send(msg, 0, msg.length, DISCOVERY_PORT, '255.255.255.255', () => {});
        log.info(`[DISCOVERY] Broadcasting ip=${ip} port=${port}`);
      });
    }, 2000);
  });
  discoverySocket.on('error', (err) => {
    console.warn('[DISCOVERY] broadcast error:', err.message);
  });
}

function stopDiscoveryBroadcast() {
  if (discoveryTimer) { clearInterval(discoveryTimer); discoveryTimer = null; }
  if (discoverySocket) { try { discoverySocket.close(); } catch {} discoverySocket = null; }
}
const { autoUpdater }  = require('electron-updater');
const log              = require('electron-log');

const isDev = process.env.NODE_ENV === 'development' || !app.isPackaged;
const allowDevTools = process.env.OPEN_ELECTRON_DEVTOOLS === '1';

// ─── Auto-Updater Config ────────────────────────────────────────────────────
// Log ke file: %USERPROFILE%\AppData\Roaming\LabKom Admin\logs\main.log
autoUpdater.logger         = log;
autoUpdater.logger.transports.file.level = 'info';
autoUpdater.autoDownload   = false;  // Admin: download manual oleh kepala lab
autoUpdater.autoInstallOnAppQuit = false;

// Kirim status update ke renderer
function sendUpdateStatus(data) {
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send('update-status', data);
  }
}

autoUpdater.on('checking-for-update', () => {
  log.info('[UPDATE] Memeriksa pembaruan…');
  sendUpdateStatus({ state: 'checking' });
});
autoUpdater.on('update-available', (info) => {
  log.info('[UPDATE] Pembaruan tersedia:', info.version);
  sendUpdateStatus({ state: 'available', version: info.version, releaseNotes: info.releaseNotes });
});
autoUpdater.on('update-not-available', (info) => {
  log.info('[UPDATE] Sudah versi terbaru:', info.version);
  sendUpdateStatus({ state: 'latest', version: info.version });
});
autoUpdater.on('download-progress', (progress) => {
  sendUpdateStatus({
    state:   'downloading',
    percent: Math.round(progress.percent),
    speed:   Math.round(progress.bytesPerSecond / 1024),  // KB/s
    total:   Math.round(progress.total / 1024 / 1024),    // MB
  });
});
autoUpdater.on('update-downloaded', (info) => {
  log.info('[UPDATE] Download selesai, siap install:', info.version);
  sendUpdateStatus({ state: 'downloaded', version: info.version });
});
autoUpdater.on('error', (err) => {
  log.error('[UPDATE] Error:', err.message);
  sendUpdateStatus({ state: 'error', message: err.message });
});

let mainWindow;
let serverProcess = null;
let serverStatus  = 'starting';  // 'starting' | 'online' | 'error'
let serverPort    = 3001;
let serverRestartTimer = null;
let isQuitting = false;
let serverStopRequested = false;

// ─── Dapatkan semua IP LAN aktif (non-internal IPv4) ──────────────────────────
function getAllLanIps() {
  const ifaces = os.networkInterfaces();
  const ips = [];
  for (const name of Object.keys(ifaces)) {
    for (const iface of ifaces[name]) {
      if (iface.family === 'IPv4' && !iface.internal) {
        ips.push(iface.address);
      }
    }
  }
  return ips.length > 0 ? ips : ['127.0.0.1'];
}

function getLanIp() {
  return getAllLanIps()[0];
}

function sendServerStatus(status = serverStatus) {
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send('server-status', { status, ip: getLanIp(), port: serverPort });
  }
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function clearServerRestartTimer() {
  if (serverRestartTimer) {
    clearTimeout(serverRestartTimer);
    serverRestartTimer = null;
  }
}

// ─── Spawn Express server ───────────────────────────────────────────────────
async function isServerRunning() {
  return new Promise((resolve) => {
    const req = http.request(
      { host: '127.0.0.1', port: serverPort, path: '/', method: 'GET' },
      (res) => { resolve(res.statusCode < 500); }
    );
    req.setTimeout(1500, () => { req.destroy(); resolve(false); });
    req.on('error', () => resolve(false));
    req.end();
  });
}

async function waitForServerReady(timeoutMs = 15000) {
  const startedAt = Date.now();
  while (Date.now() - startedAt < timeoutMs) {
    if (await isServerRunning()) return true;
    await delay(500);
  }
  return false;
}

function findNodeExecutable() {
  // 1. Coba 'node' dari PATH (dev / node terinstall)
  const { execSync } = require('child_process');
  try {
    const located = execSync('where node', { timeout: 3000 }).toString().trim().split('\n')[0].trim();
    if (located && fs.existsSync(located)) return located;
  } catch {}
  // 2. Lokasi umum Node.js di Windows
  const candidates = [
    'C:\\Program Files\\nodejs\\node.exe',
    'C:\\Program Files (x86)\\nodejs\\node.exe',
    path.join(process.env.APPDATA || '', '..', 'Local', 'Programs', 'node', 'node.exe'),
    path.join(process.env.ProgramFiles || 'C:\\Program Files', 'nodejs', 'node.exe'),
  ];
  for (const c of candidates) {
    if (fs.existsSync(c)) return c;
  }
  return 'node'; // fallback
}

function scheduleServerRestart(reason) {
  if (isQuitting || serverStopRequested || serverRestartTimer) return;
  log.warn(`[SERVER] Menjadwalkan restart otomatis: ${reason}`);
  serverRestartTimer = setTimeout(async () => {
    serverRestartTimer = null;
    if (isQuitting) return;
    await startServer();
  }, 2500);
}

async function stopManagedServer() {
  clearServerRestartTimer();
  if (!serverProcess) return;

  const processToStop = serverProcess;
  serverStopRequested = true;

  await new Promise((resolve) => {
    let settled = false;
    const finish = () => {
      if (settled) return;
      settled = true;
      resolve();
    };

    processToStop.once('close', finish);
    try { processToStop.kill(); } catch (_) { finish(); }
    setTimeout(finish, 4000);
  });

  if (serverProcess === processToStop) {
    serverProcess = null;
  }

  serverStopRequested = false;
}

async function startServer() {
  clearServerRestartTimer();
  serverStatus = 'starting';
  sendServerStatus('starting');

  // Jika server sudah berjalan (mis. dev mode), jangan spawn ulang
  if (await isServerRunning()) {
    console.log('[ADMIN] Server sudah berjalan, skip spawn.');
    serverStatus = 'online';
    serverProcess = null;
    sendServerStatus('online');
    return true;
  }

  const serverDir   = isDev
    ? path.join(__dirname, '..', '..', 'server')      // dev: C:\Labkom\server
    : path.join(process.resourcesPath, 'server');     // production build

  const serverEntry = path.join(serverDir, 'src', 'index.js');

  if (!fs.existsSync(serverEntry)) {
    console.error('[ADMIN] Server entry tidak ditemukan:', serverEntry);
    serverStatus = 'error';
    sendServerStatus('error');
    return false;
  }

  const nodeExe = findNodeExecutable();
  console.log('[ADMIN] Menjalankan server:', serverEntry, '| node:', nodeExe);

  serverStopRequested = false;
  const managedProcess = spawn(nodeExe, [serverEntry], {
    cwd:  serverDir,
    env:  { ...process.env, NODE_ENV: 'production' },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  serverProcess = managedProcess;

  managedProcess.stdout.on('data', (data) => {
    const msg = data.toString().trim();
    console.log('[SERVER]', msg);
    if (msg.includes('berjalan di port')) {
      serverStatus = 'online';
      sendServerStatus('online');
    }
  });

  managedProcess.stderr.on('data', (data) => {
    const msg = data.toString().trim();
    if (msg.includes('EADDRINUSE')) {
      console.log('[SERVER] Port sudah digunakan, server sudah berjalan.');
      serverStatus = 'online';
      sendServerStatus('online');
    } else {
      console.error('[SERVER ERR]', msg);
    }
  });

  managedProcess.on('close', (code) => {
    console.log('[SERVER] Proses berhenti dengan kode:', code);
    if (serverProcess === managedProcess) {
      serverProcess = null;
    }

    if (isQuitting || serverStopRequested) {
      serverStatus = 'stopped';
      sendServerStatus('stopped');
      return;
    }

    serverStatus = 'error';
    sendServerStatus('error');
    scheduleServerRestart(`server-exit-${code}`);
  });

  managedProcess.on('error', (err) => {
    console.error('[SERVER] Gagal menjalankan:', err.message);
    serverStatus = 'error';
    sendServerStatus('error');
    scheduleServerRestart('spawn-error');
  });

  const ready = await waitForServerReady();
  if (ready) {
    serverStatus = 'online';
    sendServerStatus('online');
    return true;
  }

  console.error('[SERVER] Backend belum sehat setelah menunggu startup.');
  serverStatus = 'error';
  sendServerStatus('error');
  scheduleServerRestart('startup-timeout');
  return false;
}

// ─── BrowserWindow ──────────────────────────────────────────────────────────
function createWindow() {
  mainWindow = new BrowserWindow({
    width:  1280,
    height: 800,
    minWidth:  900,
    minHeight: 600,
    title: 'LabKom Admin – Dashboard Manajemen Lab Komputer',
    show: false,  // Jangan tampil dulu, tunggu ready
    webPreferences: {
      preload:          path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration:  false,
      devTools:         allowDevTools,
      webSecurity:      false,  // izinkan fetch dari file:// ke http://localhost:3001
    },
  });

  // Window security features
  mainWindow.setAlwaysOnTop(true, 'screen-saver');
  mainWindow.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true });
  mainWindow.setMenuBarVisibility(false);
  mainWindow.removeMenu();

  if (isDev) {
    mainWindow.loadURL('http://localhost:5174');
    if (allowDevTools) {
      mainWindow.webContents.openDevTools({ mode: 'detach' });
    }
  } else {
    mainWindow.loadFile(path.join(__dirname, '../dist/index.html'));
  }

  // Buka link eksternal di browser default, bukan di Electron
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url);
    return { action: 'deny' };
  });

  mainWindow.on('close', (e) => {
    if (serverProcess) {
      const choice = dialog.showMessageBoxSync(mainWindow, {
        type:    'question',
        buttons: ['Ya, Keluar & Matikan Server', 'Batal'],
        defaultId: 0,
        cancelId:  1,
        title:   'Konfirmasi Keluar',
        message: 'Menutup Admin akan menghentikan server backend.\nSemua koneksi client akan terputus.',
      });
      if (choice === 1) { e.preventDefault(); return; }
    }
  });

  // Cegah minimize dan hide
  mainWindow.on('minimize', (event) => {
    event.preventDefault();
  });
  mainWindow.on('hide', () => {
    if (mainWindow && !mainWindow.isDestroyed()) {
      mainWindow.show();
    }
  });

  // Cegah window di-move atau di-resize yang tidak terotorisasi
  mainWindow.on('will-resize', (event) => {
    event.preventDefault();
  });
  mainWindow.on('move', () => {
    if (mainWindow && !mainWindow.isDestroyed()) {
      mainWindow.center();
    }
  });
}

// ─── IPC Handlers ───────────────────────────────────────────────────────────

// Renderer minta info server (IP, port, status)
ipcMain.handle('get-server-info', () => ({
  ip:     getLanIp(),
  allIps: getAllLanIps(),
  port:   serverPort,
  status: serverStatus,
}));

// Renderer minta restart server
ipcMain.handle('restart-server', async () => {
  await stopManagedServer();
  serverStatus = 'starting';
  serverProcess = null;
  await startServer();
  return { success: true };
});

// Renderer minta ping IP tertentu
ipcMain.handle('ping-server', async (_event, ip) => {
  return new Promise((resolve) => {
    const req = http.request(
      { host: ip, port: serverPort, path: '/', method: 'GET' },
      (res) => {
        let body = '';
        res.on('data', d => body += d);
        res.on('end', () => {
          try {
            const json = JSON.parse(body);
            resolve({ reachable: true, labkom: !!json.message?.includes('Labkom'), statusCode: res.statusCode });
          } catch { resolve({ reachable: true, labkom: false, statusCode: res.statusCode }); }
        });
      }
    );
    req.setTimeout(3000, () => { req.destroy(); resolve({ reachable: false, labkom: false }); });
    req.on('error', () => resolve({ reachable: false, labkom: false }));
    req.end();
  });
});

// ─── IPC Update ─────────────────────────────────────────────────────────────

// Renderer minta cek update (misal dari tombol di UI)
ipcMain.handle('check-for-updates', async () => {
  if (isDev) {
    sendUpdateStatus({ state: 'error', message: 'Cek update tidak tersedia di mode dev.' });
    return;
  }
  try { await autoUpdater.checkForUpdates(); }
  catch (e) { sendUpdateStatus({ state: 'error', message: e.message }); }
});

// Renderer minta mulai download update
ipcMain.on('download-update', () => {
  autoUpdater.downloadUpdate();
});

// Renderer minta install update sekarang (quit & install)
ipcMain.on('install-update', () => {
  autoUpdater.quitAndInstall(false, true);
});

// ─── IPC: Kirim perintah remote ke semua klien (via server) ────────────────
ipcMain.handle('send-client-cmd', async (_ev, cmd, permanent = false) => {
  return new Promise((resolve) => {
    const body = JSON.stringify({ cmd, permanent });
    const req  = http.request({
      host: '127.0.0.1', port: serverPort, path: '/api/client-cmd', method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) },
    }, (res) => {
      let b = '';
      res.on('data', d => b += d);
      res.on('end', () => { try { resolve(JSON.parse(b)); } catch { resolve({ success: false }); } });
    });
    req.on('error', () => resolve({ success: false }));
    req.setTimeout(5000, () => { req.destroy(); resolve({ success: false }); });
    req.write(body);
    req.end();
  });
});

// ─── IPC: Ambil daftar MAC address klien ────────────────────────────────────
ipcMain.handle('get-client-macs', async () => {
  return new Promise((resolve) => {
    const req = http.request({
      host: '127.0.0.1', port: serverPort, path: '/api/client-cmd/macs', method: 'GET',
    }, (res) => {
      let b = '';
      res.on('data', d => b += d);
      res.on('end', () => { try { resolve(JSON.parse(b)); } catch { resolve({ success: false, data: [] }); } });
    });
    req.on('error', () => resolve({ success: false, data: [] }));
    req.setTimeout(4000, () => { req.destroy(); resolve({ success: false, data: [] }); });
    req.end();
  });
});

// ─── IPC: Wake-on-LAN (kirim magic packet ke MAC) ────────────────────────────
ipcMain.handle('wake-on-lan', async (_ev, macAddress) => {
  return new Promise((resolve) => {
    try {
      // Bersihkan format MAC → 12 hex chars
      const hex    = macAddress.replace(/[:\-]/g, '').toUpperCase();
      if (hex.length !== 12) return resolve({ success: false, reason: 'Format MAC salah.' });
      const macBuf = Buffer.from(hex, 'hex');

      // Magic packet: 6x FF + 16x MAC
      const magic  = Buffer.alloc(6 + 16 * 6);
      magic.fill(0xff, 0, 6);
      for (let i = 0; i < 16; i++) macBuf.copy(magic, 6 + i * 6);

      const sock = dgram.createSocket({ type: 'udp4', reuseAddr: true });
      sock.once('listening', () => {
        sock.setBroadcast(true);
        sock.send(magic, 0, magic.length, 9, '255.255.255.255', (err) => {
          sock.close();
          if (err) resolve({ success: false, reason: err.message });
          else     resolve({ success: true });
        });
      });
      sock.bind();
    } catch (err) {
      resolve({ success: false, reason: err.message });
    }
  });
});

// ─── App lifecycle ──────────────────────────────────────────────────────────
app.whenReady().then(async () => {
  await startServer();   // ← Jalankan/detect backend terlebih dahulu
  startDiscoveryBroadcast(); // ← Mulai broadcast UDP agar client bisa temukan server
  createWindow();
  sendServerStatus();

  // --- Register global shortcuts untuk mencegah Alt+Tab dan shortcuts berbahaya ---
  globalShortcut.register('Alt+Tab',           () => {});
  globalShortcut.register('Shift+Alt+Tab',     () => {});
  globalShortcut.register('Alt+Esc',           () => {});
  globalShortcut.register('Alt+F4',            () => {});
  globalShortcut.register('Ctrl+Alt+Delete',   () => {});
  globalShortcut.register('Ctrl+Shift+Escape', () => {});
  globalShortcut.register('Ctrl+Esc',          () => {});
  globalShortcut.register('Meta+Tab',          () => {});
  globalShortcut.register('Shift+Meta+Tab',    () => {});
  globalShortcut.register('Meta+D',            () => {});
  globalShortcut.register('Meta+E',            () => {});
  globalShortcut.register('F11',               () => {});
  globalShortcut.register('Ctrl+R',            () => {});
  globalShortcut.register('Ctrl+W',            () => {});
  globalShortcut.register('Ctrl+F4',           () => {});
  if (!isDev) {
    globalShortcut.register('F12',             () => {});
    globalShortcut.register('Ctrl+Shift+I',    () => {});
    globalShortcut.register('Ctrl+Shift+J',    () => {});
  }

  // Auto-cek update 10 detik setelah window siap (hanya production)
  if (!isDev) {
    mainWindow.webContents.once('did-finish-load', () => {
      setTimeout(() => autoUpdater.checkForUpdates(), 10_000);
    });
  }
});

app.on('window-all-closed', async () => {
  isQuitting = true;
  stopDiscoveryBroadcast();
  await stopManagedServer();
  app.quit();
});

app.on('will-quit', () => {
  isQuitting = true;
  clearServerRestartTimer();
  if (serverProcess) { try { serverProcess.kill(); } catch {} }
});
