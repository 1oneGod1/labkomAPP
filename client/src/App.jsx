import React, { useState, useEffect, useCallback, useRef } from 'react';
import { Monitor, User, Key, Wifi, WifiOff, AlertCircle, Server, ArrowRight, RefreshCw } from 'lucide-react';
import LogoutWidget from './LogoutWidget.jsx';
import AdminExitDialog from './AdminExitDialog.jsx';
import CheckConditionForm from './CheckConditionForm.jsx';
import PostSessionCheck from './PostSessionCheck.jsx';
import { apiCall } from './api.js';

// ── Mode layar ──────────────────────────────────────────────────────
// 'loading'   → menunggu load konfigurasi server dari storage
// 'setup'     → belum ada server URL, tampilkan form konfigurasi
// 'login'     → form login kiosk fullscreen
// 'precheck'  → form checklist kondisi fasilitas sebelum sesi (setelah login)
// 'widget'    → widget logout kecil pojok layar
// 'postcheck' → form checklist akhir sesi sebelum logout
const MODE_LOADING   = 'loading';
const MODE_SETUP     = 'setup';
const MODE_LOGIN     = 'login';
const MODE_PRECHECK  = 'precheck';
const MODE_WIDGET    = 'widget';
const MODE_POSTCHECK = 'postcheck';

export default function App() {
  const [mode,           setMode]           = useState(MODE_LOADING);
  const [serverUrl,      setServerUrl]      = useState('');
  const [setupInput,     setSetupInput]     = useState('');
  const [setupError,     setSetupError]     = useState('');
  const [setupChecking,  setSetupChecking]  = useState(false);
  const [nis,            setNis]            = useState('');
  const [password,       setPassword]       = useState('');
  const [time,           setTime]           = useState(new Date());
  const [isLoading,      setIsLoading]      = useState(false);
  const [error,          setError]          = useState('');
  const [serverOnline,   setServerOnline]   = useState(false);
  const [pcName,         setPcName]         = useState('PC-LAB-??');
  const [studentData,    setStudentData]    = useState(null);
  const [showAdminDialog, setShowAdminDialog] = useState(false);
  const [cornerClicks,   setCornerClicks]   = useState(0);
  const [discoveredServers, setDiscoveredServers] = useState([]);
  const autoSwitchingServerRef = useRef(false);

  const persistServerUrl = useCallback((nextUrl) => {
    const normalized = nextUrl?.trim().replace(/\/$/, '');
    if (!normalized) return;
    setServerUrl(normalized);
    setSetupInput(normalized);
    window.electronAPI?.saveServerUrl?.(normalized);
  }, []);

  // ── Load konfigurasi server URL dari Electron userData ──────────
  useEffect(() => {
    async function loadConfig() {
      if (window.electronAPI?.getServerUrl) {
        const stored = await window.electronAPI.getServerUrl();
        if (stored) {
          persistServerUrl(stored);
          setMode(MODE_LOGIN);
        } else {
          setMode(MODE_SETUP);
        }
      } else {
        // Berjalan di browser biasa (dev tanpa Electron) → pakai localhost
        persistServerUrl('http://localhost:3001');
        setMode(MODE_LOGIN);
      }
    }
    loadConfig();
  }, [persistServerUrl]);

  // ── Ambil nama PC via Electron IPC ──────────────────────────────
  useEffect(() => {
    if (window.electronAPI?.getPcName) {
      window.electronAPI.getPcName().then(setPcName);
    } else {
      setPcName('PC-BROWSER-DEV');
    }
  }, []);

  // ── Update jam setiap detik ─────────────────────────────────────
  useEffect(() => {
    const timer = setInterval(() => setTime(new Date()), 1000);
    return () => clearInterval(timer);
  }, []);

  // ── Cek status server setiap 5 detik ───────────────────────────
  const checkServer = useCallback(async () => {
    if (!serverUrl) return;
    try {
      // Gunakan IPC (Node.js http) — lebih andal dari fetch di kiosk file://
      if (window.electronAPI?.verifyServer) {
        const result = await window.electronAPI.verifyServer(serverUrl);
        setServerOnline(result.ok);
      } else {
        // Fallback dev/browser
        const res = await fetch(`${serverUrl}/`, { signal: AbortSignal.timeout(3000) });
        setServerOnline(res.ok);
      }
    } catch {
      setServerOnline(false);
    }
  }, [serverUrl]);

  useEffect(() => {
    checkServer();
    const interval = setInterval(checkServer, 5000);
    return () => clearInterval(interval);
  }, [checkServer]);

  // ── Listener IPC dari Electron ──────────────────────────────────
  useEffect(() => {
    window.electronAPI?.onKioskOff((data) => {
      setStudentData(data);
      setMode(MODE_PRECHECK);  // ← tampilkan form checklist awal, bukan langsung widget
    });
    window.electronAPI?.onReturnToLogin(() => {
      setMode(MODE_LOGIN);
      setNis('');
      setPassword('');
      setError('');
      setStudentData(null);
    });
    // Dengarkan server yang ditemukan via UDP broadcast
    window.electronAPI?.onServerDiscovered?.((data) => {
      setDiscoveredServers((prev) => {
        if (prev.find(s => s.url === data.url)) return prev; // deduplicate
        return [...prev, data];
      });
    });
    return () => {
      window.electronAPI?.removeAllListeners('kiosk-off');
      window.electronAPI?.removeAllListeners('return-to-login');
      window.electronAPI?.removeAllListeners('server-discovered');
    };
  }, []);

  useEffect(() => {
    if (serverOnline || discoveredServers.length === 0 || !window.electronAPI?.verifyServer) return;
    if (autoSwitchingServerRef.current) return;

    let cancelled = false;
    autoSwitchingServerRef.current = true;

    const tryDiscoveredServers = async () => {
      try {
        for (const candidate of discoveredServers) {
          const candidateUrl = candidate?.url?.trim().replace(/\/$/, '');
          if (!candidateUrl || candidateUrl === serverUrl) continue;

          const result = await window.electronAPI.verifyServer(candidateUrl);
          if (cancelled) return;

          if (result.ok && result.labkom) {
            persistServerUrl(candidateUrl);
            setSetupError('');
            setServerOnline(true);
            setMode(MODE_LOGIN);
            return;
          }
        }
      } finally {
        if (!cancelled) {
          autoSwitchingServerRef.current = false;
        }
      }
    };

    tryDiscoveredServers();
    return () => {
      cancelled = true;
      autoSwitchingServerRef.current = false;
    };
  }, [discoveredServers, persistServerUrl, serverOnline, serverUrl]);

  // ── Shortcut Ctrl+Alt+Q → buka dialog admin ─────────────────────
  // Di-handle oleh globalShortcut main.js (lebih andal di kiosk mode)
  // Fallback: window keydown juga tetap aktif
  useEffect(() => {
    // Listener dari main process (globalShortcut)
    window.electronAPI?.onShowAdminDialog?.(() => setShowAdminDialog(true));

    // Fallback keyboard di renderer
    const handler = (e) => {
      if (e.ctrlKey && e.altKey && e.key.toLowerCase() === 'q') {
        e.preventDefault();
        setShowAdminDialog(true);
      }
    };
    window.addEventListener('keydown', handler);
    return () => {
      window.removeEventListener('keydown', handler);
      window.electronAPI?.removeAllListeners('show-admin-dialog');
    };
  }, []);

  // ── Klik 5x pojok kiri bawah → buka dialog admin ────────────────
  const handleCornerClick = useCallback(() => {
    setCornerClicks((n) => {
      const next = n + 1;
      if (next >= 5) {
        setShowAdminDialog(true);
        return 0;
      }
      // Reset counter setelah 3 detik tidak klik lagi
      setTimeout(() => setCornerClicks(0), 3000);
      return next;
    });
  }, []);

  // ── Handler Login ───────────────────────────────────────────────
  const handleLogin = async (e) => {
    e.preventDefault();
    if (!serverOnline) {
      setError('Server tidak dapat dijangkau. Hubungi teknisi lab.');
      return;
    }
    setIsLoading(true);
    setError('');

    try {
      const result = await apiCall(`${serverUrl}/api/auth/login`, {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify({ nis, password, pc_name: pcName }),
      });

      if (result.ok && result.data?.success) {
        const data = result.data;
        sessionStorage.setItem('session_id',   data.data.session_id);
        sessionStorage.setItem('student_name', data.data.nama_lengkap);
        if (window.electronAPI?.loginSuccess) {
          window.electronAPI.loginSuccess(data.data);
        } else {
          setStudentData(data.data);
          setMode(MODE_WIDGET);
        }
      } else {
        setError(result.data?.message || 'Login gagal. Coba lagi.');
      }
    } catch (err) {
      setError('Tidak bisa terhubung ke server. Periksa koneksi jaringan.');
    } finally {
      setIsLoading(false);
    }
  };

  const formattedTime = time.toLocaleTimeString('id-ID', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  const formattedDate = time.toLocaleDateString('id-ID', { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' });

  // ── Setelah login, simpan serverUrl ke sessionStorage (untuk LogoutWidget) ──
  // dan juga simpan agar tersedia di komponen turunan
  if (mode !== MODE_LOADING) {
    sessionStorage.setItem('server_url', serverUrl);
  }

  // ── Mode Loading ─────────────────────────────────────────────────
  if (mode === MODE_LOADING) {
    return (
      <div className="min-h-screen bg-slate-900 flex items-center justify-center">
        <div className="flex flex-col items-center space-y-4 text-white">
          <Monitor className="w-16 h-16 text-blue-400 animate-pulse" />
          <p className="text-lg text-slate-300">Memuat konfigurasi...</p>
        </div>
      </div>
    );
  }

  // ── Mode Setup (belum ada server URL) ────────────────────────────
  if (mode === MODE_SETUP) {
    const handleSetupConnect = async (e) => {
      e.preventDefault();
      setSetupError('');
      if (!setupInput.trim()) return setSetupError('Masukkan alamat IP server.');
      // Normalkan: tambahkan http:// jika tidak ada
      let url = setupInput.trim();
      if (!url.startsWith('http')) url = `http://${url}`;
      // Hilangkan trailing slash
      url = url.replace(/\/$/, '');
      // Tambah port default jika tidak ada
      if (!/:\d+$/.test(url)) url = `${url}:3001`;

      setSetupChecking(true);
      try {
        // Verifikasi via main process (bypass Chromium fetch restrictions)
        const result = window.electronAPI?.verifyServer
          ? await window.electronAPI.verifyServer(url)
          : { ok: true, labkom: true };

        if (result.ok && result.labkom) {
          window.electronAPI?.saveServerUrl?.(url);
          setServerUrl(url);
          setMode(MODE_LOGIN);
        } else if (result.ok) {
          setSetupError('Server merespons tapi bukan Labkom Server. Periksa IP & port.');
        } else {
          setSetupError(`Tidak bisa terhubung ke ${url}. Pastikan server admin sudah berjalan.`);
        }
      } catch {
        setSetupError(`Tidak bisa terhubung ke ${url}. Pastikan server admin sudah berjalan dan IP benar.`);
      } finally {
        setSetupChecking(false);
      }
    };

    return (
      <div className="min-h-screen bg-slate-900 flex items-center justify-center p-6 font-sans">
        <div className="w-full max-w-md bg-slate-800 border border-slate-700 rounded-3xl shadow-2xl p-10">
          <div className="flex flex-col items-center mb-8">
            <div className="w-20 h-20 bg-blue-600 rounded-2xl flex items-center justify-center mb-4 shadow-lg shadow-blue-600/30">
              <Server className="w-10 h-10 text-white" />
            </div>
            <h1 className="text-2xl font-bold text-white text-center">Konfigurasi Server</h1>
            <p className="text-slate-400 text-sm text-center mt-2">
              Masukkan alamat IP komputer Admin (tempat aplikasi admin dijalankan)
            </p>
          </div>

          {/* ── Auto-discovered servers ── */}
          {discoveredServers.length > 0 && (
            <div className="mb-5">
              <p className="text-xs font-semibold text-emerald-400 uppercase tracking-wider mb-2 flex items-center space-x-1.5">
                <Wifi className="w-3.5 h-3.5" />
                <span>Server ditemukan di jaringan</span>
              </p>
              <div className="space-y-2">
                {discoveredServers.map((s) => (
                  <button
                    key={s.url}
                    type="button"
                    disabled={setupChecking}
                    onClick={async () => {
                      setSetupError('');
                      setSetupChecking(true);
                      try {
                        // Verifikasi via main process (bypass Chromium fetch restrictions)
                        const result = window.electronAPI?.verifyServer
                          ? await window.electronAPI.verifyServer(s.url)
                          : { ok: true, labkom: true }; // fallback: trust UDP discovery

                        if (result.ok) {
                          window.electronAPI?.saveServerUrl?.(s.url);
                          setServerUrl(s.url);
                          setMode(MODE_LOGIN);
                        } else {
                          setSetupError(`Tidak bisa terhubung ke ${s.url}.`);
                        }
                      } catch {
                        // Jika semua gagal, trust saja server yang ditemukan via UDP
                        window.electronAPI?.saveServerUrl?.(s.url);
                        setServerUrl(s.url);
                        setMode(MODE_LOGIN);
                      } finally {
                        setSetupChecking(false);
                      }
                    }}
                    className="w-full flex items-center justify-between px-4 py-3 bg-emerald-600/20 hover:bg-emerald-600/30 border border-emerald-500/40 rounded-xl text-left transition-all group"
                  >
                    <div>
                      <p className="text-sm font-semibold text-emerald-300">{s.name}</p>
                      <p className="text-xs text-emerald-500 font-mono">{s.url}</p>
                    </div>
                    <ArrowRight className="w-4 h-4 text-emerald-400 group-hover:translate-x-1 transition-transform" />
                  </button>
                ))}
              </div>
              <div className="my-4 flex items-center space-x-3">
                <div className="flex-1 h-px bg-slate-700" />
                <span className="text-xs text-slate-500">atau isi manual</span>
                <div className="flex-1 h-px bg-slate-700" />
              </div>
            </div>
          )}
          {discoveredServers.length === 0 && (
            <div className="mb-4 flex items-center space-x-2 text-sm text-slate-500">
              <RefreshCw className="w-4 h-4 animate-spin" />
              <span>Mencari server di jaringan...</span>
            </div>
          )}

          <form onSubmit={handleSetupConnect} className="space-y-4">
            <div>
              <label className="text-sm font-medium text-slate-300 block mb-1.5">Alamat IP Server Admin</label>
              <input
                type="text"
                value={setupInput}
                onChange={e => setSetupInput(e.target.value)}
                placeholder="Contoh: 192.168.1.10"
                className="w-full px-4 py-3.5 bg-slate-700 border border-slate-600 text-white rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent outline-none transition-all text-lg font-mono"
                autoFocus
                disabled={setupChecking}
              />
              <p className="text-xs text-slate-500 mt-1.5">Port default: 3001 (otomatis ditambahkan)</p>
            </div>

            {setupError && (
              <div className="bg-red-500/20 border border-red-500/50 text-red-300 p-3 rounded-xl flex items-start space-x-2 text-sm">
                <AlertCircle className="w-5 h-5 flex-shrink-0 mt-0.5" />
                <span>{setupError}</span>
              </div>
            )}

            <button
              type="submit"
              disabled={setupChecking}
              className="w-full py-4 bg-blue-600 hover:bg-blue-500 disabled:bg-blue-600/40 text-white font-semibold rounded-xl transition-all flex items-center justify-center space-x-2"
            >
              {setupChecking
                ? <><RefreshCw className="w-5 h-5 animate-spin" /><span>Menghubungkan...</span></>
                : <><span>Hubungkan ke Server</span><ArrowRight className="w-5 h-5" /></>
              }
            </button>
          </form>

          <div className="mt-6 p-4 bg-slate-700/50 rounded-xl border border-slate-700">
            <p className="text-xs text-slate-400 font-medium mb-2">Tips:</p>
            <ol className="text-xs text-slate-500 space-y-1 list-decimal list-inside">
              <li>Pastikan aplikasi Admin sudah dibuka di PC kepala lab</li>
              <li>Server akan muncul otomatis di atas dalam beberapa detik</li>
              <li>Klik nama server untuk langsung terhubung</li>
            </ol>
          </div>

          {/* Tombol keluar — hanya di setup screen sebelum login */}
          <button
            type="button"
            onClick={() => window.electronAPI?.exitApp?.()}
            className="mt-4 w-full py-2.5 text-sm text-slate-500 hover:text-slate-300 hover:bg-slate-700 rounded-xl transition-all"
          >
            Keluar Aplikasi
          </button>
        </div>
      </div>
    );
  }

  // ── Mode Widget (pasca-login) ───────────────────────────────
  if (mode === MODE_PRECHECK) {
    return (
      <CheckConditionForm
        studentData={studentData}
        serverUrl={serverUrl}
        pcName={pcName}
        onComplete={() => setMode(MODE_WIDGET)}
      />
    );
  }

  // ── Mode Widget (pasca-login) ────────────────────────────────────
  if (mode === MODE_WIDGET) {
    return (
      <LogoutWidget
        studentData={studentData}
        onRequestPostCheck={() => {
          // Perluas jendela ke ukuran checklist sebelum tampilkan form post-sesi
          window.electronAPI?.resizeWindow('checklist');
          setMode(MODE_POSTCHECK);
        }}
        onLogoutComplete={() => {
          // Fallback jika tidak pakai Electron (browser dev)
          if (!window.electronAPI?.doLogout) {
            setMode(MODE_LOGIN);
            setNis('');
            setPassword('');
            setError('');
            setStudentData(null);
          }
        }}
      />
    );
  }

  // ── Mode Post-Check (form checklist akhir sesi) ──────────────────────
  if (mode === MODE_POSTCHECK) {
    return (
      <PostSessionCheck
        studentData={studentData}
        serverUrl={serverUrl}
        onLogoutConfirmed={async () => {
          // Jalankan logout ke server lalu kembali ke kiosk
          try {
            const sessionId = sessionStorage.getItem('session_id');
            await apiCall(`${serverUrl}/api/auth/logout`, {
              method:  'POST',
              headers: { 'Content-Type': 'application/json' },
              body:    JSON.stringify({ session_id: Number(sessionId) }),
            });
          } catch (_) {}
          sessionStorage.clear();
          window.electronAPI?.doLogout();
          if (!window.electronAPI?.doLogout) {
            setMode(MODE_LOGIN);
            setStudentData(null);
          }
        }}
      />
    );
  }

  // ── Mode Login (kiosk fullscreen) ────────────────────────────────
  return (
    <div className="min-h-screen bg-slate-900 flex items-center justify-center p-4 relative overflow-hidden font-sans">

      {/* Dialog Admin (overlay di atas semua) */}
      {showAdminDialog && (
        <AdminExitDialog onClose={() => setShowAdminDialog(false)} />
      )}

      {/* Tombol tersembunyi pojok kiri bawah — klik 5x untuk buka dialog admin */}
      <button
        onClick={handleCornerClick}
        className="absolute bottom-0 left-0 w-12 h-12 z-20 opacity-0"
        tabIndex={-1}
        aria-hidden="true"
      />
      {/* Background */}
      <div className="absolute inset-0 opacity-20">
        <div className="absolute inset-0 bg-gradient-to-br from-blue-600 via-indigo-900 to-slate-900 mix-blend-multiply" />
        <div className="w-full h-full bg-[url('https://images.unsplash.com/photo-1550751827-4bd374c3f58b?q=80&w=2070&auto=format&fit=crop')] bg-cover bg-center" />
      </div>

      {/* Top Bar */}
      <div className="absolute top-0 left-0 right-0 p-6 flex justify-between items-start z-10 text-white drop-shadow-md">
        <div className="flex items-center space-x-3">
          <Monitor className="w-8 h-8 text-blue-400" />
          <div>
            <h1 className="text-2xl font-bold tracking-wider">{pcName}</h1>
            <p className="text-sm text-slate-300">Lab Komputer Jaringan</p>
          </div>
        </div>
        <div className="flex flex-col items-end space-y-1">
          <div className={`flex items-center space-x-2 font-medium ${serverOnline ? 'text-green-400' : 'text-red-400'}`}>
            {serverOnline
              ? <><Wifi className="w-5 h-5" /><span>Terhubung ke Server</span></>
              : <><WifiOff className="w-5 h-5" /><span>Server Offline</span></>
            }
          </div>
        </div>
      </div>

      {/* Login Card */}
      <div className="relative z-10 w-full max-w-4xl grid grid-cols-1 md:grid-cols-2 rounded-3xl overflow-hidden shadow-2xl bg-white/10 backdrop-blur-md border border-white/20">

        {/* Kiri - Jam & Info */}
        <div className="p-10 flex flex-col justify-between text-white bg-gradient-to-br from-blue-600/50 to-indigo-900/50">
          <div>
            <img
              src="https://static.wixstatic.com/media/07639e_83549958900b44ad9fea05d99e380dd5~mv2.png/v1/fill/w_559,h_512,al_c/07639e_83549958900b44ad9fea05d99e380dd5~mv2.png"
              alt="Logo Sekolah"
              className="w-20 h-20 object-contain mb-6 filter brightness-0 invert opacity-80"
            />
            <h2 className="text-3xl font-bold mb-2">Sistem Manajemen Lab</h2>
            <p className="text-blue-200">Silakan login untuk mulai menggunakan komputer ini.</p>
          </div>
          <div className="mt-12">
            <div className="text-6xl font-light tracking-tighter mb-2">{formattedTime}</div>
            <div className="text-lg text-blue-200 font-medium">{formattedDate}</div>
          </div>
        </div>

        {/* Kanan - Form Login */}
        <div className="p-10 bg-slate-900/80 flex flex-col justify-center">
          <div className="text-center mb-8">
            <h3 className="text-2xl font-semibold text-white">Login Siswa</h3>
            <p className="text-slate-400 mt-1">Gunakan NIS yang terdaftar</p>
          </div>

          <form onSubmit={handleLogin} className="space-y-6">
            {/* Error message */}
            {error && (
              <div className="bg-red-500/20 border border-red-500/50 text-red-200 p-3 rounded-xl flex items-center space-x-2 text-sm">
                <AlertCircle className="w-5 h-5 flex-shrink-0" />
                <span>{error}</span>
              </div>
            )}

            {/* Input NIS */}
            <div className="space-y-1">
              <label className="text-sm font-medium text-slate-300 ml-1">Nomor Induk Siswa (NIS)</label>
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-4 flex items-center pointer-events-none">
                  <User className="h-5 w-5 text-slate-500" />
                </div>
                <input
                  type="text"
                  value={nis}
                  onChange={(e) => setNis(e.target.value)}
                  className="w-full pl-12 pr-4 py-4 bg-slate-800/50 border border-slate-700 text-white rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent outline-none transition-all"
                  placeholder="Masukkan NIS..."
                  required
                  autoComplete="off"
                  disabled={isLoading}
                />
              </div>
            </div>

            {/* Input Password */}
            <div className="space-y-1">
              <label className="text-sm font-medium text-slate-300 ml-1">Password</label>
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-4 flex items-center pointer-events-none">
                  <Key className="h-5 w-5 text-slate-500" />
                </div>
                <input
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="w-full pl-12 pr-4 py-4 bg-slate-800/50 border border-slate-700 text-white rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent outline-none transition-all"
                  placeholder="Masukkan Password..."
                  required
                  disabled={isLoading}
                />
              </div>
            </div>

            {/* Tombol Login */}
            <button
              type="submit"
              disabled={isLoading || !serverOnline}
              className={`w-full py-4 rounded-xl text-white font-semibold text-lg transition-all shadow-lg ${
                isLoading || !serverOnline
                  ? 'bg-blue-600/40 cursor-not-allowed'
                  : 'bg-blue-600 hover:bg-blue-500 hover:shadow-blue-500/25 active:scale-[0.98]'
              }`}
            >
              {isLoading ? (
                <span className="flex items-center justify-center">
                  <svg className="animate-spin -ml-1 mr-3 h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                  </svg>
                  Memverifikasi...
                </span>
              ) : !serverOnline ? (
                'Server Tidak Tersedia'
              ) : (
                'Masuk ke Komputer'
              )}
            </button>
          </form>

          <div className="mt-8 text-center text-sm text-slate-500">
            <p>Butuh bantuan? Silakan hubungi Teknisi Lab.</p>
            <p className="mt-1">Versi 1.0.0</p>
          </div>
        </div>
      </div>
    </div>
  );
}
