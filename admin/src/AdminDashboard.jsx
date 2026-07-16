import React, { useState, useEffect, useCallback, useRef } from 'react';
import { io } from 'socket.io-client';
import {
  Monitor, Users, History, LogOut, Search, Plus, Edit, Trash2,
  Power, AlertTriangle, CheckCircle2, X, ShieldCheck, Settings,
  Volume2, VolumeX, Globe, Image as ImageIcon, Moon, Lock,
  RefreshCw, Loader2, Save, ChevronLeft, ChevronRight,
  Wifi, WifiOff, Server, Copy, Check, DownloadCloud, Bell, Zap,
  ClipboardList, FilterX, ThumbsUp, ThumbsDown, Eye, EyeOff, Maximize2,
  PowerOff, Play, Radio, Cpu, Activity, MessageCircle,
  BookOpen, FileText, FolderOpen, LayoutGrid, List, Upload, Trophy,
} from 'lucide-react';
import StudentModal from './components/StudentModal.jsx';
import ActivityMonitor from './components/ActivityMonitor.jsx';
import ChatPanel from './components/ChatPanel.jsx';
import ScreenShareAdmin from './components/ScreenShareAdmin.jsx';
import AttentionModeButton from './components/AttentionModeButton.jsx';

// Di Electron production, window load dari file:// sehingga fetch relatif gagal.
// Deteksi protokol: file:// → pakai absolute URL ke server lokal.
const API = (typeof window !== 'undefined' && window.location.protocol === 'file:')
  ? 'http://localhost:3001'
  : '';  // dev mode: Vite proxy arahkan /api → localhost:3001
const REALTIME_API = API || 'http://localhost:3001';

function screenFrameToBlob(frame, mime = 'image/jpeg') {
  if (!frame) return null;
  if (frame instanceof Blob) return frame;
  if (frame instanceof ArrayBuffer) return new Blob([frame], { type: mime });
  if (ArrayBuffer.isView(frame)) {
    return new Blob([frame.buffer.slice(frame.byteOffset, frame.byteOffset + frame.byteLength)], { type: mime });
  }
  if (frame.type === 'Buffer' && Array.isArray(frame.data)) {
    return new Blob([new Uint8Array(frame.data)], { type: mime });
  }
  return null;
}

// ─── Banner IP Server (tampil di header) ──────────────────────────────────
function ServerInfoBanner({ info }) {
  const [copied, setCopied] = useState(false);
  if (!info) return null;
  const url = `http://${info.ip}:${info.port}`;
  const copyUrl = () => {
    navigator.clipboard.writeText(info.ip);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };
  return (
    <div className={`flex items-center space-x-2 px-3 py-1.5 rounded-xl border text-sm font-medium ${
      info.status === 'online'
        ? 'bg-emerald-50 border-emerald-200 text-emerald-700'
        : info.status === 'starting'
        ? 'bg-amber-50 border-amber-200 text-amber-700'
        : 'bg-red-50 border-red-200 text-red-700'
    }`}>
      {info.status === 'online'
        ? <Wifi className="w-4 h-4" />
        : info.status === 'starting'
        ? <Loader2 className="w-4 h-4 animate-spin" />
        : <WifiOff className="w-4 h-4" />}
      <div className="flex flex-col leading-none">
        <span className="text-xs opacity-70">IP Server untuk Client PC:</span>
        <span className="font-mono font-bold tracking-wide">{info.ip}:{info.port}</span>
      </div>
      <button
        onClick={copyUrl}
        title="Salin IP"
        className="ml-1 p-1 rounded hover:bg-black/10 transition-colors"
      >
        {copied ? <Check className="w-3.5 h-3.5" /> : <Copy className="w-3.5 h-3.5" />}
      </button>
    </div>
  );
}

// ─── Banner Update Aplikasi ────────────────────────────────────────────────
function UpdateBanner({ status, onCheck, onDownload, onInstall }) {
  if (!status || status.state === 'latest') return null;

  if (status.state === 'checking') {
    return (
      <div className="bg-slate-100 border-b border-slate-200 px-6 py-2.5 flex items-center space-x-2 text-sm text-slate-600">
        <Loader2 className="w-4 h-4 animate-spin text-slate-400" />
        <span>Memeriksa pembaruan…</span>
      </div>
    );
  }

  if (status.state === 'available') {
    return (
      <div className="bg-amber-50 border-b border-amber-200 px-6 py-2.5 flex items-center justify-between">
        <div className="flex items-center space-x-3 text-sm">
          <Bell className="w-4 h-4 text-amber-600" />
          <span className="font-medium text-amber-800">Versi baru tersedia: <strong>v{status.version}</strong></span>
        </div>
        <button
          onClick={onDownload}
          className="flex items-center space-x-2 px-4 py-1.5 bg-amber-500 hover:bg-amber-600 text-white text-sm font-medium rounded-lg transition-colors"
        >
          <DownloadCloud className="w-4 h-4" /><span>Unduh Sekarang</span>
        </button>
      </div>
    );
  }

  if (status.state === 'downloading') {
    return (
      <div className="bg-blue-50 border-b border-blue-200 px-6 py-2.5 flex items-center space-x-4">
        <DownloadCloud className="w-4 h-4 text-blue-600 flex-shrink-0" />
        <div className="flex-1 min-w-0">
          <div className="flex items-center justify-between text-sm mb-1">
            <span className="text-blue-800 font-medium">Mengunduh pembaruan…</span>
            <span className="text-blue-600 font-mono text-xs">{status.percent}% · {status.speed} KB/s · {status.total} MB</span>
          </div>
          <div className="w-full bg-blue-200 rounded-full h-1.5">
            <div
              className="bg-blue-600 h-1.5 rounded-full transition-all duration-300"
              style={{ width: `${status.percent}%` }}
            />
          </div>
        </div>
      </div>
    );
  }

  if (status.state === 'downloaded') {
    return (
      <div className="bg-emerald-50 border-b border-emerald-200 px-6 py-2.5 flex items-center justify-between">
        <div className="flex items-center space-x-3 text-sm">
          <CheckCircle2 className="w-4 h-4 text-emerald-600" />
          <span className="font-medium text-emerald-800">Pembaruan <strong>v{status.version}</strong> siap diinstall!</span>
        </div>
        <button
          onClick={onInstall}
          className="flex items-center space-x-2 px-4 py-1.5 bg-emerald-600 hover:bg-emerald-700 text-white text-sm font-medium rounded-lg transition-colors"
        >
          <Zap className="w-4 h-4" /><span>Install &amp; Restart</span>
        </button>
      </div>
    );
  }

  if (status.state === 'error') {
    return (
      <div className="bg-red-50 border-b border-red-200 px-6 py-2 flex items-center justify-between text-sm">
        <div className="flex items-center space-x-2 text-red-700">
          <AlertTriangle className="w-4 h-4" />
          <span>Cek update gagal: {status.message}</span>
        </div>
        <button onClick={onCheck} className="text-red-600 hover:underline text-xs font-medium">Coba Lagi</button>
      </div>
    );
  }

  return null;
}

// ─── Utility ───────────────────────────────────────────────────────────────
async function apiFetch(path, opts = {}) {
  const token = sessionStorage.getItem('admin_token');
  const extraHeaders = token ? { Authorization: `Bearer ${token}` } : {};
  const res = await fetch(API + path, {
    headers: { 'Content-Type': 'application/json', ...extraHeaders, ...(opts.headers || {}) },
    ...opts,
  });
  if (res.status === 401) {
    sessionStorage.removeItem('admin_token');
  }
  return res.json();
}

// ─── Toast sederhana ───────────────────────────────────────────────────────
function Toast({ message, type, onClose }) {
  useEffect(() => {
    const t = setTimeout(onClose, 3500);
    return () => clearTimeout(t);
  }, [onClose]);
  const colors = type === 'error'
    ? 'bg-red-600 text-white'
    : 'bg-emerald-600 text-white';
  return (
    <div className={`fixed bottom-6 right-6 z-[100] px-5 py-3 rounded-xl shadow-xl flex items-center space-x-3 animate-in zoom-in-95 duration-500 ${colors}`}>
      <span className="text-sm font-medium">{message}</span>
      <button onClick={onClose}><X className="w-4 h-4" /></button>
    </div>
  );
}

// ═══════════════════════════════════════════════════════════════════════════
export default function AdminDashboard() {
  const [adminPassword, setAdminPassword] = useState('');
  const [authReady, setAuthReady] = useState(false);
  const [authLoading, setAuthLoading] = useState(true);
  const [authError, setAuthError] = useState('');
  const [activeTab, setActiveTab] = useState('monitoring');
  const [currentTime, setCurrentTime] = useState(new Date());
  const [serverInfo, setServerInfo]   = useState(null);
  const [updateStatus, setUpdateStatus] = useState(null); // { state, version, percent, ... }

  // Toast
  const [toast, setToast] = useState(null);
  const showToast = (message, type = 'success') => setToast({ message, type });

  const handleAdminLogin = async (e) => {
    e.preventDefault();
    if (!adminPassword.trim()) return;
    setAuthLoading(true);
    setAuthError('');
    try {
      const res = await fetch(`${API}/api/admin/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ password: adminPassword }),
      });
      const data = await res.json();
      if (res.ok && data.success && data.token) {
        sessionStorage.setItem('admin_token', data.token);
        setAuthReady(true);
        setAdminPassword('');
      } else {
        setAuthError(data.message || 'Login admin gagal.');
      }
    } catch {
      setAuthError('Tidak bisa terhubung ke server.');
    } finally {
      setAuthLoading(false);
    }
  };

  const handleAdminLogout = async () => {
    const token = sessionStorage.getItem('admin_token');
    if (token) {
      try {
        await fetch(`${API}/api/admin/logout`, {
          method: 'POST',
          headers: { Authorization: `Bearer ${token}` },
        });
      } catch { /* ignore */ }
    }
    sessionStorage.removeItem('admin_token');
    setAuthReady(false);
    setAuthError('');
    setAdminPassword('');
  };

  const refreshAdminToken = useCallback(async () => {
    const token = sessionStorage.getItem('admin_token');
    if (!token) return false;
    try {
      const res = await fetch(`${API}/api/admin/refresh`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
      });
      const data = await res.json();
      if (res.ok && data.success && data.token) {
        sessionStorage.setItem('admin_token', data.token);
        return true;
      }
    } catch {
      // ignore
    }
    sessionStorage.removeItem('admin_token');
    setAuthReady(false);
    return false;
  }, []);

  useEffect(() => {
    async function bootstrapAuth() {
      const token = sessionStorage.getItem('admin_token');
      if (!token) {
        setAuthReady(false);
        setAuthLoading(false);
        return;
      }
      try {
        const res = await fetch(`${API}/api/admin/me`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        const data = await res.json();
        setAuthReady(Boolean(res.ok && data.success));
        if (!res.ok) sessionStorage.removeItem('admin_token');
      } catch {
        setAuthReady(false);
      } finally {
        setAuthLoading(false);
      }
    }
    bootstrapAuth();
  }, []);

  useEffect(() => {
    if (!authReady) return;
    const t = setInterval(() => {
      if (!sessionStorage.getItem('admin_token')) {
        setAuthReady(false);
      }
    }, 1000);
    return () => clearInterval(t);
  }, [authReady]);

  useEffect(() => {
    if (!authReady) return;
    const refreshInterval = setInterval(() => {
      refreshAdminToken();
    }, 20 * 60 * 1000);
    return () => clearInterval(refreshInterval);
  }, [authReady, refreshAdminToken]);

  // ── Load server info dari Electron IPC (hanya jika berjalan di Electron) ──
  useEffect(() => {
    async function loadServerInfo() {
      if (window.electronAPI?.getServerInfo) {
        const info = await window.electronAPI.getServerInfo();
        setServerInfo(info);
        // Listener update realtime
        window.electronAPI.onServerStatus((data) => setServerInfo(prev => ({ ...prev, ...data })));
      }
    }
    loadServerInfo();
    return () => window.electronAPI?.removeAllListeners?.('server-status');
  }, []);

  // ── Auto-Update IPC ──────────────────────────────────────────────────────
  useEffect(() => {
    if (window.electronAPI?.onUpdateStatus) {
      window.electronAPI.onUpdateStatus((data) => setUpdateStatus(data));
    }
    return () => window.electronAPI?.removeAllListeners?.('update-status');
  }, []);

  const handleCheckUpdate = () => {
    setUpdateStatus(null);
    window.electronAPI?.checkForUpdates?.();
  };
  const handleDownloadUpdate = () => window.electronAPI?.downloadUpdate?.();
  const handleInstallUpdate  = () => window.electronAPI?.installUpdate?.();

  // ── Screen Share ─────────────────────────────────────────────────
  const [screens, setScreens]           = useState([]);
  const [screensLoading, setScreensLoading] = useState(false);
  const [focusedScreen, setFocusedScreen]   = useState(null); // pc_name for fullscreen modal
  const [focusedDisplayId, setFocusedDisplayId] = useState(null);
  const realtimeSocketRef = useRef(null);
  const focusedScreenRef = useRef(null);
  const focusedDisplayRef = useRef(null);
  const screensRequestRef = useRef(null);
  const screenObjectUrlsRef = useRef(new Map());

  const revokeScreenObjectUrl = useCallback((pcName) => {
    const currentUrl = screenObjectUrlsRef.current.get(pcName);
    if (!currentUrl) return;
    URL.revokeObjectURL(currentUrl);
    screenObjectUrlsRef.current.delete(pcName);
  }, []);

  const releaseAllScreenObjectUrls = useCallback(() => {
    for (const url of screenObjectUrlsRef.current.values()) {
      URL.revokeObjectURL(url);
    }
    screenObjectUrlsRef.current.clear();
  }, []);

  const fetchScreens = useCallback(async (silent = false) => {
    if (!silent) setScreensLoading(true);
    screensRequestRef.current?.abort?.();
    const controller = new AbortController();
    screensRequestRef.current = controller;

    try {
      const token = sessionStorage.getItem('admin_token');
      const res  = await fetch(`${API}/api/screens`, {
        cache: 'no-store',
        headers: token ? { Authorization: `Bearer ${token}` } : {},
        signal: controller.signal,
      });
      if (res.status === 401) {
        sessionStorage.removeItem('admin_token');
        setAuthReady(false);
      }
      const data = await res.json();
      if (data.success) {
        releaseAllScreenObjectUrls();
        setScreens(data.data || []);
      }
    } catch (err) {
      if (err?.name !== 'AbortError') {
        console.warn('fetchScreens failed:', err);
      }
    } finally {
      if (screensRequestRef.current === controller) {
        screensRequestRef.current = null;
      }
      if (!silent) setScreensLoading(false);
    }
  }, [releaseAllScreenObjectUrls]);

  useEffect(() => {
    if (activeTab === 'screens') {
      fetchScreens();
    }
    return () => {
      screensRequestRef.current?.abort?.();
    };
  }, [activeTab, fetchScreens]);

  useEffect(() => {
    focusedScreenRef.current = focusedScreen;
    focusedDisplayRef.current = focusedDisplayId;
  }, [focusedScreen, focusedDisplayId]);

  useEffect(() => {
    if (activeTab !== 'screens') {
      setFocusedScreen(null);
      setFocusedDisplayId(null);
    }
  }, [activeTab]);

  // ── Monitoring ────────────────────────────────────────────────────────
  const [pcs,        setPcs]        = useState([]);
  const [pcsLoading, setPcsLoading] = useState(true);
  const [showLogoutModal, setShowLogoutModal] = useState(false);
  const [selectedPc,      setSelectedPc]      = useState(null);
  const [controlModalPc,  setControlModalPc]  = useState(null);
  const [confirmLogoutAll, setConfirmLogoutAll] = useState(false);
  const pollingRef = useRef(null);
  const monitoringRefreshTimerRef = useRef(null);

  const fetchPcs = useCallback(async (silent = false) => {
    if (!silent) setPcsLoading(true);
    try {
      const data = await apiFetch('/api/monitoring/pcs');
      if (data.success) setPcs(data.data);
    } catch { /* ignore polling errors */ }
    finally { if (!silent) setPcsLoading(false); }
  }, []);

  // Poll setiap 5 detik saat tab monitoring aktif
  useEffect(() => {
    if (activeTab === 'monitoring') {
      fetchPcs();
      pollingRef.current = setInterval(() => fetchPcs(true), 5000);
    }
    return () => clearInterval(pollingRef.current);
  }, [activeTab, fetchPcs]);

  const handleForceLogout = (pc) => {
    setSelectedPc(pc);
    setShowLogoutModal(true);
  };

  const confirmForceLogout = async () => {
    try {
      const data = await apiFetch('/api/monitoring/force-logout', {
        method: 'POST',
        body: JSON.stringify({ pc_name: selectedPc.id }),
      });
      if (data.success) {
        showToast(`${selectedPc.id}: ${selectedPc.student?.name} berhasil dikeluarkan.`);
        fetchPcs(true);
      } else {
        showToast(data.message, 'error');
      }
    } catch { showToast('Koneksi ke server gagal.', 'error'); }
    setShowLogoutModal(false);
    setSelectedPc(null);
    setControlModalPc(null);
  };

  const handleForceLogoutAll = async () => {
    try {
      const data = await apiFetch('/api/monitoring/force-logout-all', { method: 'POST' });
      if (data.success) {
        showToast(data.message);
        fetchPcs(true);
      } else showToast(data.message, 'error');
    } catch { showToast('Koneksi ke server gagal.', 'error'); }
    setConfirmLogoutAll(false);
  };

  // ── Remote Power Control ─────────────────────────────────────────────
  const [remoteBusy,  setRemoteBusy]  = useState(false);
  const [clientMacs,  setClientMacs]  = useState([]);           // [{pc_name, mac, ip}]
  const [wolBusy,     setWolBusy]     = useState({});           // {pc_name: bool}
  const [confirmKillAll, setConfirmKillAll] = useState(null);   // null | 'temp' | 'perm'
  const [mappingBusy, setMappingBusy] = useState({});
  const [mappingSelections, setMappingSelections] = useState({});

  const refreshClientMacs = useCallback(async () => {
    if (window.electronAPI?.getClientMacs) {
      const res = await window.electronAPI.getClientMacs(sessionStorage.getItem('admin_token'));
      if (res.success) setClientMacs(res.data || []);
    }
  }, []);

  useEffect(() => {
    if (activeTab === 'monitoring') refreshClientMacs();
  }, [activeTab, refreshClientMacs]);

  useEffect(() => {
    if (!authReady) {
      realtimeSocketRef.current?.disconnect?.();
      realtimeSocketRef.current = null;
      return;
    }

    const token = sessionStorage.getItem('admin_token');
    if (!token) return;

    const socket = io(REALTIME_API, {
      transports: ['websocket', 'polling'],
      auth: {
        role: 'admin',
        token,
        capabilities: {
          student_screen_binary_v1: true,
        },
      },
    });

    realtimeSocketRef.current = socket;

    socket.on('connect', () => {
      if (focusedScreenRef.current) {
        socket.emit('admin:watch-screen', {
          pc_name: focusedScreenRef.current,
          display_id: focusedDisplayRef.current,
        });
      }
    });

    socket.on('screens:snapshot', (data = []) => {
      releaseAllScreenObjectUrls();
      setScreens(Array.isArray(data) ? data : []);
    });

    socket.on('screen:update', (screen) => {
      if (!screen?.pc_name) return;
      revokeScreenObjectUrl(screen.pc_name);
      setScreens((prev) => {
        const next = prev.filter((item) => item.pc_name !== screen.pc_name);
        next.push(screen);
        next.sort((a, b) => a.pc_name.localeCompare(b.pc_name));
        return next;
      });
    });

    socket.on('screen:update-v2', (screen) => {
      if (!screen?.pc_name) return;
      const blob = screenFrameToBlob(screen.frame, screen.mime);
      if (!blob) return;

      const nextUrl = URL.createObjectURL(blob);
      const previousUrl = screenObjectUrlsRef.current.get(screen.pc_name);
      screenObjectUrlsRef.current.set(screen.pc_name, nextUrl);
      if (previousUrl) {
        setTimeout(() => URL.revokeObjectURL(previousUrl), 250);
      }

      const now = Date.now();
      const nextScreen = {
        ...screen,
        image: nextUrl,
        transport: 'binary',
        latency: Math.max(0, now - (Number(screen.captured_at) || now)),
        updated_at: Number(screen.received_at) || now,
      };
      delete nextScreen.frame;

      setScreens((prev) => {
        const next = prev.filter((item) => item.pc_name !== screen.pc_name);
        next.push(nextScreen);
        next.sort((a, b) => a.pc_name.localeCompare(b.pc_name));
        return next;
      });
    });

    socket.on('screen:remove', ({ pc_name } = {}) => {
      if (!pc_name) return;
      revokeScreenObjectUrl(pc_name);
      setScreens((prev) => prev.filter((item) => item.pc_name !== pc_name));
      setFocusedScreen((prev) => (prev === pc_name ? null : prev));
      if (focusedScreenRef.current === pc_name) setFocusedDisplayId(null);
    });

    socket.on('presence:update', () => {
      if (monitoringRefreshTimerRef.current) return;
      monitoringRefreshTimerRef.current = setTimeout(() => {
        monitoringRefreshTimerRef.current = null;
        fetchPcs(true);
        refreshClientMacs();
      }, 400);
    });

    socket.on('connect_error', (err) => {
      console.warn('realtime socket failed:', err?.message || err);
    });

    return () => {
      if (monitoringRefreshTimerRef.current) {
        clearTimeout(monitoringRefreshTimerRef.current);
        monitoringRefreshTimerRef.current = null;
      }
      socket.emit('admin:stop-watch-screen');
      socket.disconnect();
      releaseAllScreenObjectUrls();
      if (realtimeSocketRef.current === socket) {
        realtimeSocketRef.current = null;
      }
    };
  }, [authReady, fetchPcs, refreshClientMacs, releaseAllScreenObjectUrls, revokeScreenObjectUrl]);

  useEffect(() => {
    const socket = realtimeSocketRef.current;
    if (!socket) return undefined;

    if (focusedScreen) {
      socket.emit('admin:watch-screen', {
        pc_name: focusedScreen,
        display_id: focusedDisplayId,
      });
    } else {
      socket.emit('admin:stop-watch-screen');
    }

    return () => {
      if (!focusedScreen) return;
      socket.emit('admin:stop-watch-screen');
    };
  }, [focusedScreen, focusedDisplayId]);

  const handleKillAll = async (permanent) => {
    setRemoteBusy(true);
    setConfirmKillAll(null);
    try {
      if (window.electronAPI?.sendClientCmd) {
        const res = await window.electronAPI.sendClientCmd('kill', permanent, sessionStorage.getItem('admin_token'));
        if (res.success) showToast(`Perintah hentikan dikirim (${permanent ? 'permanen' : 'sementara'}).`);
        else showToast('Gagal kirim perintah kill.', 'error');
      } else {
        await apiFetch('/api/client-cmd', { method: 'POST', body: JSON.stringify({ cmd: 'kill', permanent }) });
        showToast('Perintah kill dikirim via server.');
      }
    } catch { showToast('Gagal mengirim perintah.', 'error'); }
    setRemoteBusy(false);
  };

  const handleEnableAll = async () => {
    setRemoteBusy(true);
    try {
      if (window.electronAPI?.sendClientCmd) {
        const res = await window.electronAPI.sendClientCmd('enable', false, sessionStorage.getItem('admin_token'));
        if (res.success) showToast('Perintah aktifkan dikirim — klien akan restart dlm ≤2 menit.');
        else showToast('Gagal kirim perintah enable.', 'error');
      } else {
        await apiFetch('/api/client-cmd', { method: 'POST', body: JSON.stringify({ cmd: 'enable' }) });
        showToast('Perintah enable dikirim.');
      }
    } catch { showToast('Gagal mengirim perintah.', 'error'); }
    setRemoteBusy(false);
  };

  const handleWakeOnLan = async (mac, pc_name) => {
    setWolBusy(prev => ({ ...prev, [pc_name]: true }));
    try {
      let res;
      if (window.electronAPI?.wakeOnLan) {
        res = await window.electronAPI.wakeOnLan(mac);
      } else {
        showToast('WoL hanya tersedia di aplikasi Electron.', 'error');
        return;
      }
      if (res.success) showToast(`Magic packet dikirim ke ${pc_name} (${mac}).`);
      else showToast(`WoL gagal: ${res.reason}`, 'error');
    } catch { showToast('Gagal mengirim WoL.', 'error'); }
    setWolBusy(prev => ({ ...prev, [pc_name]: false }));
  };

  // ── Students ─────────────────────────────────────────────────────────
  const handleAssignDeviceMapping = async (pc) => {
    const sourceKey = pc.actual_pc_name || pc.id;
    const targetPcName = mappingSelections[sourceKey];
    if (!targetPcName) {
      showToast('Pilih PC lab tujuan terlebih dahulu.', 'error');
      return;
    }

    setMappingBusy(prev => ({ ...prev, [sourceKey]: true }));
    try {
      const data = await apiFetch('/api/monitoring/map-device', {
        method: 'POST',
        body: JSON.stringify({
          target_pc_name: targetPcName,
          source_pc_name: sourceKey,
          source_mac: pc.mac || null,
          source_ip: pc.ip || null,
        }),
      });

      if (data.success) {
        showToast(`Perangkat ${sourceKey} dipetakan ke ${targetPcName}.`);
        setMappingSelections((prev) => {
          const next = { ...prev };
          delete next[sourceKey];
          return next;
        });
        fetchPcs(true);
        refreshClientMacs();
      } else {
        showToast(data.message || 'Gagal memetakan perangkat.', 'error');
      }
    } catch {
      showToast('Koneksi ke server gagal.', 'error');
    }
    setMappingBusy(prev => ({ ...prev, [sourceKey]: false }));
  };

  const handleClearDeviceMapping = async (pc) => {
    const targetPcName = pc.id;
    setMappingBusy(prev => ({ ...prev, [targetPcName]: true }));
    try {
      const data = await apiFetch('/api/monitoring/clear-mapping', {
        method: 'POST',
        body: JSON.stringify({ target_pc_name: targetPcName }),
      });

      if (data.success) {
        showToast(`Mapping perangkat untuk ${targetPcName} dilepas.`);
        fetchPcs(true);
        refreshClientMacs();
      } else {
        showToast(data.message || 'Gagal melepas mapping.', 'error');
      }
    } catch {
      showToast('Koneksi ke server gagal.', 'error');
    }
    setMappingBusy(prev => ({ ...prev, [targetPcName]: false }));
  };

  const [students,     setStudents]     = useState([]);
  const [stuLoading,   setStuLoading]   = useState(false);
  const [stuSearch,    setStuSearch]    = useState('');
  const [stuModal,     setStuModal]     = useState(null); // null | 'add' | student obj
  const [deleteTarget, setDeleteTarget] = useState(null);

  const fetchStudents = useCallback(async () => {
    setStuLoading(true);
    try {
      const data = await apiFetch('/api/students');
      if (data.success) setStudents(data.data);
    } catch { showToast('Gagal memuat data siswa.', 'error'); }
    finally { setStuLoading(false); }
  }, []);

  useEffect(() => {
    if (activeTab === 'students') fetchStudents();
  }, [activeTab, fetchStudents]);

  const confirmDeleteStudent = async () => {
    try {
      const data = await apiFetch(`/api/students/${deleteTarget.id}`, { method: 'DELETE' });
      if (data.success) { showToast(data.message); fetchStudents(); }
      else showToast(data.message, 'error');
    } catch { showToast('Koneksi ke server gagal.', 'error'); }
    setDeleteTarget(null);
  };

  const filteredStudents = students.filter(s =>
    s.nis.includes(stuSearch) ||
    s.nama_lengkap.toLowerCase().includes(stuSearch.toLowerCase())
  );

  // ── History ───────────────────────────────────────────────────────────
  const [history,      setHistory]    = useState([]);
  const [histLoading,  setHistLoading]= useState(false);
  const [histDate,     setHistDate]   = useState('');
  const [histPage,     setHistPage]   = useState(1);
  const [histTotal,    setHistTotal]  = useState(0);
  const HIST_LIMIT = 20;

  const fetchHistory = useCallback(async (pg = 1) => {
    setHistLoading(true);
    try {
      const params = new URLSearchParams({ page: pg, limit: HIST_LIMIT });
      if (histDate) params.set('date', histDate);
      const data = await apiFetch(`/api/history?${params}`);
      if (data.success) {
        setHistory(data.data);
        setHistTotal(data.total);
        setHistPage(pg);
      }
    } catch { showToast('Gagal memuat riwayat.', 'error'); }
    finally { setHistLoading(false); }
  }, [histDate]);

  useEffect(() => {
    if (activeTab === 'history') fetchHistory(1);
  }, [activeTab, fetchHistory]);

  // ── Control Settings ──────────────────────────────────────────────────
  const [ctrlLoading, setCtrlLoading] = useState(false);
  const [ctrlSaving,  setCtrlSaving]  = useState(false);
  const [globalVolume,     setGlobalVolume]     = useState(75);
  const [isGlobalMuted,    setIsGlobalMuted]    = useState(false);
  const [webFilterEnabled, setWebFilterEnabled] = useState(true);
  const [webFilterMode,    setWebFilterMode]    = useState('blacklist');
  const [whitelist,        setWhitelist]        = useState([]);
  const [blacklist,        setBlacklist]        = useState([]);
  const [newWebsite,       setNewWebsite]       = useState('');
  const [newBlockedWeb,    setNewBlockedWeb]    = useState('');
  const [wallpaperUrl,     setWallpaperUrl]     = useState('');
  const [wallpaperTarget,  setWallpaperTarget]  = useState('both');

  const loadSettings = useCallback(async () => {
    setCtrlLoading(true);
    try {
      const data = await apiFetch('/api/control/settings');
      if (data.success) {
        const s = data.data;
        setGlobalVolume(Number(s.master_volume ?? 75));
        setIsGlobalMuted(s.master_muted === true || s.master_muted === 'true');
        setWebFilterEnabled(s.web_filter_enabled !== false && s.web_filter_enabled !== 'false');
        setWebFilterMode(s.web_filter_mode || 'blacklist');
        setWhitelist(Array.isArray(s.whitelist) ? s.whitelist : []);
        setBlacklist(Array.isArray(s.blacklist) ? s.blacklist : []);
        setWallpaperUrl(s.wallpaper_url || '');
        setWallpaperTarget(s.wallpaper_target || 'both');
      }
    } catch { showToast('Gagal memuat pengaturan.', 'error'); }
    finally { setCtrlLoading(false); }
  }, []);

  useEffect(() => {
    if (activeTab === 'control') loadSettings();
  }, [activeTab, loadSettings]);

  const saveSettings = async (extra = {}) => {
    setCtrlSaving(true);
    try {
      const payload = {
        master_volume:      String(globalVolume),
        master_muted:       String(isGlobalMuted),
        web_filter_enabled: String(webFilterEnabled),
        web_filter_mode:    webFilterMode,
        whitelist:          JSON.stringify(whitelist),
        blacklist:          JSON.stringify(blacklist),
        wallpaper_url:      wallpaperUrl,
        wallpaper_target:   wallpaperTarget,
        ...extra,
      };
      const data = await apiFetch('/api/control/settings', {
        method: 'POST',
        body: JSON.stringify(payload),
      });
      if (data.success) showToast('Pengaturan berhasil disimpan.');
      else showToast(data.message, 'error');
    } catch { showToast('Koneksi ke server gagal.', 'error'); }
    finally { setCtrlSaving(false); }
  };

  // Jam realtime
  useEffect(() => {
    const t = setInterval(() => setCurrentTime(new Date()), 1000);
    return () => clearInterval(t);
  }, []);

  // ── Pengecekan Fasilitas ──────────────────────────────────────────────
  const [checks,       setChecks]      = useState([]);
  const [chkLoading,   setChkLoading]  = useState(false);
  const [chkDate,      setChkDate]     = useState('');
  const [chkType,      setChkType]     = useState('');  // '' | 'pre' | 'post'
  const [chkPage,      setChkPage]     = useState(1);
  const [chkTotal,     setChkTotal]    = useState(0);
  const [expandedChk,  setExpandedChk] = useState(null); // id row yang di-expand
  const CHK_LIMIT = 30;

  // ── Server Tab State ────────────────────────────────────────────────────
  const [pingResults, setPingResults] = useState({});
  const [restarting,  setRestarting]  = useState(false);
  const [copiedIp,    setCopiedIp]    = useState(null);
  const [deviceClaims, setDeviceClaims] = useState([]);
  const [claimsLoading, setClaimsLoading] = useState(false);

  const fetchDeviceClaims = useCallback(async () => {
    setClaimsLoading(true);
    try {
      const data = await apiFetch('/api/admin/device-claims');
      if (data?.success) setDeviceClaims(data.data || []);
    } catch (_) {}
    finally { setClaimsLoading(false); }
  }, []);

  const revokeDeviceClaim = useCallback(async (pcName) => {
    if (!window.confirm(`Hapus claim untuk ${pcName}? Device tersebut akan otomatis register ulang saat reconnect.`)) return;
    try {
      const data = await apiFetch('/api/admin/device-claims/revoke', {
        method: 'POST',
        body: JSON.stringify({ pc_name: pcName }),
      });
      if (data?.success) {
        showToast(data.message || 'Claim dihapus', 'success');
        fetchDeviceClaims();
      } else {
        showToast(data?.message || 'Gagal menghapus claim', 'error');
      }
    } catch (e) {
      showToast('Gagal menghapus claim: ' + e.message, 'error');
    }
  }, [fetchDeviceClaims]);

  useEffect(() => {
    if (activeTab === 'server' && authReady) fetchDeviceClaims();
  }, [activeTab, authReady, fetchDeviceClaims]);

  const fetchChecks = useCallback(async (pg = 1) => {
    setChkLoading(true);
    try {
      const params = new URLSearchParams({ page: pg, limit: CHK_LIMIT });
      if (chkDate) params.set('date', chkDate);
      if (chkType) params.set('type', chkType);
      const data = await apiFetch(`/api/checks?${params}`);
      if (data.success) {
        setChecks(data.data);
        setChkTotal(data.total);
        setChkPage(pg);
      }
    } catch { showToast('Gagal memuat log pengecekan.', 'error'); }
    finally { setChkLoading(false); }
  }, [chkDate, chkType]);

  useEffect(() => {
    if (activeTab === 'checks') fetchChecks(1);
  }, [activeTab, fetchChecks]);

  // ═══════════════════════════════════════════════════════════════════════
  // RENDER PENGECEKAN FASILITAS
  // ═══════════════════════════════════════════════════════════════════════
  const renderChecks = () => {
    const chkTotalPages = Math.ceil(chkTotal / CHK_LIMIT);

    // Label item berdasarkan type & key
    const itemLabels = {
      cpu_status:          'PC / Keyboard / Mouse',
      monitor_status:      'Monitor & Kabel',
      desk_status:         'Meja & Kursi',
      hw_status:           'Perangkat Keras',
      cleanliness_status:  'Kebersihan & Kerapian',
      account_status:      'Akun Pribadi (Log Out)',
      system_status:       'Sistem & Desktop',
      file_status:         'File & Riwayat Browser',
    };
    const noteKey = (k) => k.replace('_status', '_note');

    return (
      <div className="space-y-6 animate-in fade-in duration-500">
        {/* Header & Filter */}
        <div className="flex flex-wrap items-end gap-3">
          <div className="flex items-center space-x-2">
            <ClipboardList className="w-5 h-5 text-violet-600" />
            <h3 className="text-lg font-semibold text-slate-800">Log Pengecekan Fasilitas</h3>
          </div>
          <div className="ml-auto flex flex-wrap items-center gap-2">
            <input type="date" value={chkDate} onChange={e => { setChkDate(e.target.value); setChkPage(1); }}
              className="px-3 py-2 border border-slate-200 rounded-lg text-sm focus:ring-2 focus:ring-violet-400 outline-none" />
            <select value={chkType} onChange={e => { setChkType(e.target.value); setChkPage(1); }}
              className="px-3 py-2 border border-slate-200 rounded-lg text-sm focus:ring-2 focus:ring-violet-400 outline-none bg-white">
              <option value="">Semua Tipe</option>
              <option value="pre">Awal Sesi (Pre)</option>
              <option value="post">Akhir Sesi (Post)</option>
            </select>
            {(chkDate || chkType) && (
              <button onClick={() => { setChkDate(''); setChkType(''); }}
                className="flex items-center space-x-1 px-3 py-2 text-slate-400 hover:text-slate-600 text-sm">
                <FilterX className="w-4 h-4" /><span>Reset</span>
              </button>
            )}
            <button onClick={() => fetchChecks(chkPage)}
              className="flex items-center space-x-1.5 px-3 py-2 bg-violet-600 hover:bg-violet-700 text-white rounded-lg text-sm font-medium transition-colors">
              <RefreshCw className="w-4 h-4" /><span>Refresh</span>
            </button>
          </div>
        </div>

        {/* Tabel */}
        <div className="bg-white rounded-2xl border border-slate-200 shadow-sm overflow-hidden">
          {chkLoading ? (
            <div className="p-16 flex items-center justify-center">
              <Loader2 className="w-8 h-8 text-violet-500 animate-spin" />
            </div>
          ) : checks.length === 0 ? (
            <div className="p-16 text-center">
              <ClipboardList className="w-12 h-12 text-slate-200 mx-auto mb-3" />
              <p className="text-slate-400 text-sm">Belum ada data pengecekan{chkDate ? ` pada tanggal ${chkDate}` : ''}.</p>
            </div>
          ) : (
            <>
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="bg-slate-50 border-b border-slate-200">
                    <tr>
                      <th className="p-4 text-left font-semibold text-slate-600 w-36">Waktu</th>
                      <th className="p-4 text-left font-semibold text-slate-600">Siswa</th>
                      <th className="p-4 text-left font-semibold text-slate-600 w-28">PC</th>
                      <th className="p-4 text-left font-semibold text-slate-600 w-24">Tipe</th>
                      <th className="p-4 text-left font-semibold text-slate-600 w-28">Status</th>
                      <th className="p-4 text-left font-semibold text-slate-600 w-20">Detail</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {checks.map((c) => {
                      const isOpen = expandedChk === c.id;
                      // Kumpulkan item yang bermasalah
                      const issues = Object.keys(itemLabels).filter(k => c[k] === 'bad');
                      return (
                        <React.Fragment key={c.id}>
                          <tr className={`hover:bg-slate-50 ${c.has_issue ? 'bg-red-50/30' : ''}`}>
                            <td className="p-4">
                              <p className="font-medium text-slate-700">{c.date_str}</p>
                              <p className="text-xs text-slate-400">{c.time_str}</p>
                            </td>
                            <td className="p-4">
                              <p className="font-medium text-slate-800">{c.nama_lengkap}</p>
                              <p className="text-xs text-slate-500">{c.nis}</p>
                            </td>
                            <td className="p-4">
                              <span className="font-mono text-xs bg-slate-100 px-2 py-1 rounded-md">{c.pc_name}</span>
                            </td>
                            <td className="p-4">
                              <span className={`px-2.5 py-1 text-xs font-semibold rounded-full ${
                                c.check_type === 'pre'
                                  ? 'bg-blue-100 text-blue-700'
                                  : 'bg-indigo-100 text-indigo-700'
                              }`}>
                                {c.check_type === 'pre' ? 'Awal Sesi' : 'Akhir Sesi'}
                              </span>
                            </td>
                            <td className="p-4">
                              {c.has_issue ? (
                                <div className="flex items-center space-x-1.5 text-red-600">
                                  <ThumbsDown className="w-4 h-4" />
                                  <span className="text-xs font-semibold">{issues.length} Masalah</span>
                                </div>
                              ) : (
                                <div className="flex items-center space-x-1.5 text-emerald-600">
                                  <ThumbsUp className="w-4 h-4" />
                                  <span className="text-xs font-semibold">Aman</span>
                                </div>
                              )}
                            </td>
                            <td className="p-4">
                              <button onClick={() => setExpandedChk(isOpen ? null : c.id)}
                                className="text-xs text-violet-600 hover:underline font-medium">
                                {isOpen ? 'Tutup' : 'Lihat'}
                              </button>
                            </td>
                          </tr>
                          {isOpen && (
                            <tr className="bg-slate-50">
                              <td colSpan={6} className="px-6 pb-4 pt-2">
                                <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-2">
                                  {Object.keys(itemLabels).filter(k => c[k] !== null).map(k => (
                                    <div key={k} className={`p-3 rounded-xl border text-xs ${
                                      c[k] === 'bad'
                                        ? 'bg-red-50 border-red-200'
                                        : 'bg-emerald-50 border-emerald-100'
                                    }`}>
                                      <div className="flex items-center space-x-1.5 mb-1">
                                        {c[k] === 'bad'
                                          ? <AlertTriangle className="w-3.5 h-3.5 text-red-500 flex-shrink-0" />
                                          : <CheckCircle2 className="w-3.5 h-3.5 text-emerald-500 flex-shrink-0" />}
                                        <span className={`font-semibold ${c[k] === 'bad' ? 'text-red-700' : 'text-emerald-700'}`}>
                                          {itemLabels[k]}
                                        </span>
                                      </div>
                                      {c[k] === 'bad' && c[noteKey(k)] && (
                                        <p className="text-red-600 leading-snug pl-5">{c[noteKey(k)]}</p>
                                      )}
                                    </div>
                                  ))}
                                </div>
                              </td>
                            </tr>
                          )}
                        </React.Fragment>
                      );
                    })}
                  </tbody>
                </table>
              </div>

              {/* Pagination */}
              {chkTotalPages > 1 && (
                <div className="p-4 border-t border-slate-100 flex items-center justify-between text-sm text-slate-600">
                  <span>{chkTotal} total data</span>
                  <div className="flex items-center space-x-2">
                    <button disabled={chkPage === 1} onClick={() => fetchChecks(chkPage - 1)}
                      className="p-1.5 rounded-lg hover:bg-slate-100 disabled:opacity-40 disabled:cursor-not-allowed">
                      <ChevronLeft className="w-4 h-4" />
                    </button>
                    <span className="font-medium">Hal {chkPage} / {chkTotalPages}</span>
                    <button disabled={chkPage === chkTotalPages} onClick={() => fetchChecks(chkPage + 1)}
                      className="p-1.5 rounded-lg hover:bg-slate-100 disabled:opacity-40 disabled:cursor-not-allowed">
                      <ChevronRight className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      </div>
    );
  };

  // ═══════════════════════════════════════════════════════════════════════
  // RENDER STATUS SERVER
  // ═══════════════════════════════════════════════════════════════════════
  const renderScreens = () => {
    const focusedData = focusedScreen ? screens.find(s => s.pc_name === focusedScreen) : null;
    const focusedMonitors = Array.isArray(focusedData?.monitors) ? focusedData.monitors : [];
    const closeFocusedScreen = () => {
      setFocusedScreen(null);
      setFocusedDisplayId(null);
    };
    const openFocusedScreen = (screenData) => {
      setFocusedScreen(screenData.pc_name);
      setFocusedDisplayId(screenData.display_id || screenData.monitors?.find((monitor) => monitor.primary)?.display_id || null);
    };
    return (
      <div>
        <div className="flex items-center justify-between mb-6">
          <div>
            <h3 className="text-lg font-bold text-slate-800">Layar Aktif Siswa</h3>
            <p className="text-sm text-slate-500 mt-0.5">
              {screens.length === 0
                ? 'Belum ada PC yang aktif mengirim layar.'
                : `${screens.length} PC sedang aktif - realtime low-latency`}
            </p>
          </div>
          <button
            onClick={() => fetchScreens()}
            className="flex items-center space-x-2 px-4 py-2 bg-slate-100 hover:bg-slate-200 text-slate-700 rounded-xl text-sm font-medium transition-colors"
          >
            <RefreshCw className={`w-4 h-4 ${screensLoading ? 'animate-spin' : ''}`} />
            <span>Refresh</span>
          </button>
        </div>

        {screensLoading && screens.length === 0 ? (
          <div className="flex justify-center py-20">
            <Loader2 className="w-8 h-8 animate-spin text-blue-400" />
          </div>
        ) : screens.length === 0 ? (
          <div className="text-center py-20 text-slate-400">
            <EyeOff className="w-12 h-12 mx-auto mb-3 opacity-30" />
            <p className="font-medium">Tidak ada layar aktif</p>
            <p className="text-sm mt-1">Siswa harus login agar layarnya terkirim ke sini.</p>
          </div>
        ) : (
          <div className="grid grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
            {screens.map((s) => (
              <div
                key={s.pc_name}
                className="group bg-white rounded-2xl border border-slate-200 shadow-sm overflow-hidden cursor-pointer hover:shadow-lg hover:border-blue-300 transition-all"
                onClick={() => openFocusedScreen(s)}
              >
                <div className="relative bg-slate-950 aspect-video overflow-hidden">
                  <img
                    src={s.image}
                    alt={s.pc_name}
                    className="w-full h-full object-cover"
                  />
                  <div className="absolute inset-0 flex items-center justify-center opacity-0 group-hover:opacity-100 bg-black/30 transition-opacity">
                    <Maximize2 className="w-8 h-8 text-white drop-shadow" />
                  </div>
                  <span className="absolute top-2 right-2 w-2.5 h-2.5 rounded-full bg-green-400 ring-2 ring-white" />
                </div>
                <div className="p-3">
                  <div className="flex items-center justify-between gap-2">
                    <p className="font-bold text-slate-800 text-sm truncate">{s.pc_name}</p>
                    <span className="text-[10px] font-semibold uppercase tracking-wide text-slate-400">
                      {Array.isArray(s.monitors) && s.monitors.length > 1
                        ? `${s.monitors.length} Monitor`
                        : (s.transport === 'binary' ? 'Binary' : 'Grid')}
                    </span>
                  </div>
                  {s.student_name && (
                    <p className="text-xs text-blue-600 truncate mt-0.5">{s.student_name}</p>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}

        {/* ── Modal fullscreen ── */}
        {focusedData && (
          <div
            className="fixed inset-0 z-50 bg-slate-950/90 backdrop-blur-sm flex flex-col items-center justify-center p-4 animate-in fade-in"
            onClick={closeFocusedScreen}
          >
            <div
              className="w-full max-w-5xl bg-slate-900 rounded-2xl overflow-hidden shadow-2xl animate-in zoom-in-95 duration-300"
              onClick={(e) => e.stopPropagation()}
            >
              <div className="flex items-center justify-between px-5 py-3 border-b border-slate-700">
                <div className="flex items-center space-x-3">
                  <Monitor className="w-5 h-5 text-blue-400" />
                  <div>
                    <p className="font-bold text-white">{focusedData.pc_name}</p>
                    {focusedData.student_name && (
                      <p className="text-xs text-slate-400">{focusedData.student_name}</p>
                    )}
                  </div>
                  <span className="ml-2 flex items-center space-x-1 text-xs text-green-400">
                    <span className="w-2 h-2 rounded-full bg-green-400 animate-pulse" />
                    <span>Live</span>
                  </span>
                  <span className="px-2.5 py-1 rounded-full bg-blue-500/15 text-blue-300 text-[11px] font-semibold uppercase tracking-wide">
                    Focus HQ
                  </span>
                  {focusedData.transport === 'binary' && (
                    <span className="px-2.5 py-1 rounded-full bg-emerald-500/15 text-emerald-300 text-[11px] font-semibold uppercase tracking-wide">
                      Binary · {focusedData.width || 0}×{focusedData.height || 0} · {focusedData.latency || 0} ms
                    </span>
                  )}
                </div>
                <div className="flex items-center gap-3">
                  {focusedMonitors.length > 1 && (
                    <label className="flex items-center gap-2 text-xs text-slate-300">
                      <span>Monitor</span>
                      <select
                        value={focusedDisplayId || focusedData.display_id || ''}
                        onChange={(event) => setFocusedDisplayId(event.target.value || null)}
                        className="bg-slate-800 border border-slate-600 rounded-lg px-3 py-1.5 text-white outline-none focus:border-blue-400"
                      >
                        {focusedMonitors.map((monitor) => (
                          <option key={monitor.display_id} value={monitor.display_id}>
                            {monitor.label} · {monitor.width}×{monitor.height}
                          </option>
                        ))}
                      </select>
                    </label>
                  )}
                  <button
                    onClick={closeFocusedScreen}
                    className="text-slate-400 hover:text-white p-1 rounded-lg hover:bg-slate-700 transition-colors"
                  >
                    <X className="w-5 h-5" />
                  </button>
                </div>
              </div>
              <img
                src={focusedData.image}
                alt={focusedData.pc_name}
                className="w-full object-contain bg-black"
              />
            </div>
            <p className="text-slate-500 text-sm mt-3">Klik di luar untuk tutup - Mode fokus mengaktifkan preview kualitas lebih tinggi untuk layar ini</p>
          </div>
        )}
      </div>
    );
  };

  const renderServer = () => {
    const allIps   = serverInfo?.allIps  || (serverInfo?.ip ? [serverInfo.ip] : []);
    const port     = serverInfo?.port    || 3001;
    const status   = serverInfo?.status  || 'unknown';

    const pingIp = async (ip) => {
      setPingResults(prev => ({ ...prev, [ip]: { checking: true } }));
      const res = await window.electronAPI?.pingServer?.(ip);
      setPingResults(prev => ({ ...prev, [ip]: { checking: false, ...(res || {}) } }));
    };

    const pingAll = () => allIps.forEach(pingIp);

    const handleRestart = async () => {
      setRestarting(true);
      await window.electronAPI?.restartServer?.();
      setTimeout(() => setRestarting(false), 3000);
    };

    const copyIpUrl = (ip) => {
      navigator.clipboard.writeText(`http://${ip}:${port}`);
      setCopiedIp(ip);
      setTimeout(() => setCopiedIp(null), 2000);
    };

    const statusCfg = {
      online:   { bg: 'bg-emerald-50',  border: 'border-emerald-300', dot: 'bg-emerald-500',  text: 'text-emerald-700', label: 'ONLINE',   icon: <Wifi    className="w-8 h-8 text-emerald-500" /> },
      starting: { bg: 'bg-amber-50',    border: 'border-amber-300',   dot: 'bg-amber-400',    text: 'text-amber-700',   label: 'STARTING', icon: <Loader2 className="w-8 h-8 text-amber-500 animate-spin" /> },
      error:    { bg: 'bg-red-50',      border: 'border-red-300',     dot: 'bg-red-500',      text: 'text-red-700',     label: 'ERROR',    icon: <WifiOff className="w-8 h-8 text-red-500" /> },
      unknown:  { bg: 'bg-slate-50',    border: 'border-slate-300',   dot: 'bg-slate-400',    text: 'text-slate-600',   label: 'UNKNOWN',  icon: <Server  className="w-8 h-8 text-slate-400" /> },
    };
    const cfg = statusCfg[status] || statusCfg.unknown;

    return (
      <div className="space-y-6">
        {/* ── Kartu Status Utama ── */}
        <div className={`${cfg.bg} ${cfg.border} border-2 rounded-2xl p-6 flex items-center gap-6 shadow-sm`}>
          <div className="flex-shrink-0 p-4 bg-white rounded-2xl shadow-sm">{cfg.icon}</div>
          <div className="flex-1">
            <p className="text-xs font-semibold tracking-widest uppercase text-slate-400 mb-1">Status Server Labkom</p>
            <div className="flex items-center gap-2">
              <span className={`inline-block w-3 h-3 rounded-full ${cfg.dot} animate-pulse`} />
              <span className={`text-3xl font-black ${cfg.text}`}>{cfg.label}</span>
            </div>
            <p className="text-sm text-slate-500 mt-1">Port: <span className="font-mono font-bold text-slate-700">{port}</span></p>
          </div>
          <div className="flex flex-col gap-2">
            <button
              onClick={pingAll}
              className="flex items-center gap-2 px-4 py-2 bg-white border border-slate-200 rounded-xl text-sm font-medium text-slate-700 hover:bg-slate-50 hover:shadow transition-all"
            >
              <RefreshCw className="w-4 h-4" /> Cek Semua
            </button>
            <button
              onClick={handleRestart}
              disabled={restarting}
              className="flex items-center gap-2 px-4 py-2 bg-amber-500 text-white rounded-xl text-sm font-medium hover:bg-amber-600 disabled:opacity-60 transition-all"
            >
              {restarting ? <Loader2 className="w-4 h-4 animate-spin" /> : <Power className="w-4 h-4" />}
              {restarting ? 'Restarting…' : 'Restart Server'}
            </button>
          </div>
        </div>

        {/* ── Daftar IP Jaringan ── */}
        <div className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
          <div className="px-6 py-4 border-b border-slate-100 flex items-center justify-between">
            <div>
              <h3 className="font-bold text-slate-800 text-lg">Alamat IP Jaringan</h3>
              <p className="text-sm text-slate-500 mt-0.5">Gunakan salah satu URL berikut pada konfigurasi Client PC</p>
            </div>
            <span className="text-xs bg-slate-100 text-slate-500 rounded-full px-3 py-1 font-medium">{allIps.length} adaptor</span>
          </div>
          <div className="divide-y divide-slate-100">
            {allIps.length === 0 && (
              <div className="px-6 py-8 text-center text-slate-400">
                <WifiOff className="w-10 h-10 mx-auto mb-3 text-slate-300" />
                <p className="text-sm">Tidak ada IP jaringan yang terdeteksi.</p>
              </div>
            )}
            {allIps.map(ip => {
              const pr     = pingResults[ip] || {};
              const url    = `http://${ip}:${port}`;
              const isPrimary = ip === serverInfo?.ip;
              return (
                <div key={ip} className="px-6 py-4 flex items-center gap-4 hover:bg-slate-50 transition-colors">
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="font-mono font-bold text-slate-800 text-base">{url}</span>
                      {isPrimary && <span className="text-xs bg-blue-100 text-blue-600 rounded-full px-2 py-0.5 font-semibold">Utama</span>}
                    </div>
                    {pr.reachable === true  && <p className="text-xs text-emerald-600 flex items-center gap-1 mt-0.5"><CheckCircle2 className="w-3.5 h-3.5" /> Dapat dijangkau{pr.labkom ? ' — LabKom API OK' : ''}</p>}
                    {pr.reachable === false && <p className="text-xs text-red-500 flex items-center gap-1 mt-0.5"><AlertTriangle className="w-3.5 h-3.5" /> Tidak dapat dijangkau dari PC ini</p>}
                  </div>
                  <div className="flex items-center gap-2">
                    <button
                      onClick={() => copyIpUrl(ip)}
                      title="Salin URL"
                      className="flex items-center gap-1.5 px-3 py-1.5 text-xs border border-slate-200 rounded-lg hover:bg-slate-100 transition-colors text-slate-600"
                    >
                      {copiedIp === ip ? <Check className="w-3.5 h-3.5 text-emerald-500" /> : <Copy className="w-3.5 h-3.5" />}
                      {copiedIp === ip ? 'Tersalin!' : 'Salin URL'}
                    </button>
                    <button
                      onClick={() => pingIp(ip)}
                      disabled={pr.checking}
                      className="flex items-center gap-1.5 px-3 py-1.5 text-xs bg-blue-50 border border-blue-200 rounded-lg hover:bg-blue-100 transition-colors text-blue-700 disabled:opacity-60"
                    >
                      {pr.checking ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Zap className="w-3.5 h-3.5" />}
                      {pr.checking ? 'Mengecek…' : 'Ping'}
                    </button>
                  </div>
                </div>
              );
            })}
          </div>
        </div>

        {/* ── Panduan Konfigurasi Client ── */}
        <div className="bg-blue-50 border border-blue-200 rounded-2xl p-5">
          <div className="flex items-start gap-3">
            <Bell className="w-5 h-5 text-blue-500 flex-shrink-0 mt-0.5" />
            <div className="text-sm text-blue-800">
              <p className="font-semibold mb-1">Cara konfigurasi Client PC:</p>
              <ol className="list-decimal list-inside space-y-1 text-blue-700">
                <li>Buka aplikasi <strong>LabKom Siswa</strong> di PC client.</li>
                <li>Pada layar pencarian, tunggu server ditemukan otomatis via UDP.</li>
                <li>Jika tidak terdeteksi otomatis, klik <strong>"Masukkan manual"</strong> dan gunakan URL dari daftar di atas.</li>
                <li>Pastikan PC client berada di jaringan LAN yang sama.</li>
              </ol>
            </div>
          </div>
        </div>
      </div>
    );
  };

  // ═══════════════════════════════════════════════════════════════════════
  // RENDER MONITORING
  // ═══════════════════════════════════════════════════════════════════════
  const renderMonitoring = () => {
    const activeCount  = pcs.filter(p => p.status === 'active').length;
    const lockedCount  = pcs.filter(p => p.status === 'locked').length;
    const offlineCount = pcs.filter(p => p.status === 'offline').length;
    const labPcOptions = pcs
      .filter((p) => !p.is_unmapped)
      .map((p) => ({ id: p.id, label: p.label || p.id }));

    return (
      <div className="space-y-6 animate-in fade-in duration-500">
        {/* Stats Cards - Enhanced */}
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-5">
          {[
            { label: 'Total PC',      value: pcs.length,   icon: Monitor,      gradient: 'from-blue-500 to-blue-600',     bg: 'bg-blue-50',    border: 'border-blue-100',  text: 'text-blue-600'  },
            { label: 'Sesi Aktif',    value: activeCount,  icon: CheckCircle2, gradient: 'from-emerald-500 to-green-600', bg: 'bg-emerald-50', border: 'border-emerald-100', text: 'text-emerald-600' },
            { label: 'Terkunci',      value: lockedCount,  icon: ShieldCheck,  gradient: 'from-amber-500 to-orange-600',  bg: 'bg-amber-50',   border: 'border-amber-100',  text: 'text-amber-600' },
            { label: 'Offline',       value: offlineCount, icon: Power,        gradient: 'from-red-500 to-rose-600',      bg: 'bg-red-50',     border: 'border-red-100',    text: 'text-red-600'   },
          ].map(({ label, value, icon: Icon, gradient, bg, border, text }) => (
            <div key={label} className={`relative overflow-hidden bg-white rounded-2xl border ${border} shadow-sm hover:shadow-lg transition-all duration-300 group`}>
              {/* Gradient background decoration */}
              <div className={`absolute inset-0 bg-gradient-to-br ${gradient} opacity-0 group-hover:opacity-5 transition-opacity duration-300`} />
              
              <div className="relative p-5">
                <div className="flex items-start justify-between mb-3">
                  <div className={`p-3 ${bg} rounded-xl ${text} shadow-sm`}>
                    <Icon className="w-6 h-6" />
                  </div>
                  <div className={`px-2.5 py-1 ${bg} ${text} rounded-full text-[10px] font-bold uppercase tracking-wider`}>
                    Live
                  </div>
                </div>
                <div>
                  <p className="text-sm text-slate-500 font-medium mb-1">{label}</p>
                  <p className={`text-3xl font-black ${text}`}>{value}</p>
                </div>
              </div>
            </div>
          ))}
        </div>

        {/* Remote Power Control panel */}
        <div className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
          <div className="px-5 py-3 bg-slate-50 border-b border-slate-200 flex items-center justify-between">
            <div className="flex items-center space-x-2">
              <Cpu className="w-4 h-4 text-slate-500" />
              <span className="text-sm font-semibold text-slate-700">Kendali Remote Komputer</span>
            </div>
            <button onClick={refreshClientMacs} className="text-xs text-slate-400 hover:text-slate-600 flex items-center space-x-1">
              <RefreshCw className="w-3 h-3" /><span>Perbarui MAC</span>
            </button>
          </div>
          <div className="p-5 flex flex-wrap gap-3 items-start">
            {/* Kill temporary */}
            <button
              onClick={() => setConfirmKillAll('temp')}
              disabled={remoteBusy}
              className="flex items-center space-x-2 px-4 py-2.5 bg-amber-50 hover:bg-amber-100 border border-amber-200 text-amber-800 rounded-xl text-sm font-medium transition-colors disabled:opacity-50"
            >
              <PowerOff className="w-4 h-4" />
              <span>Hentikan Semua (Sementara)</span>
            </button>
            {/* Kill permanent */}
            <button
              onClick={() => setConfirmKillAll('perm')}
              disabled={remoteBusy}
              className="flex items-center space-x-2 px-4 py-2.5 bg-red-50 hover:bg-red-100 border border-red-200 text-red-700 rounded-xl text-sm font-medium transition-colors disabled:opacity-50"
            >
              <PowerOff className="w-4 h-4" />
              <span>Hentikan Semua (Permanen)</span>
            </button>
            {/* Enable */}
            <button
              onClick={handleEnableAll}
              disabled={remoteBusy}
              className="flex items-center space-x-2 px-4 py-2.5 bg-emerald-50 hover:bg-emerald-100 border border-emerald-200 text-emerald-700 rounded-xl text-sm font-medium transition-colors disabled:opacity-50"
            >
              <Play className="w-4 h-4" />
              <span>Aktifkan Semua Client</span>
            </button>
            {/* WoL per PC */}
            {clientMacs.length > 0 && (
              <div className="w-full border-t border-slate-100 pt-3 mt-1">
                <p className="text-xs font-semibold text-slate-500 mb-2 uppercase tracking-wider">Wake-on-LAN per PC</p>
                <div className="flex flex-wrap gap-2">
                  {clientMacs.map(({ pc_name, mac, ip }) => (
                    <button
                      key={pc_name}
                      onClick={() => handleWakeOnLan(mac, pc_name)}
                      disabled={wolBusy[pc_name]}
                      title={`MAC: ${mac} | IP: ${ip || '?'}`}
                      className="flex items-center space-x-1.5 px-3 py-1.5 bg-blue-50 hover:bg-blue-100 border border-blue-200 text-blue-700 rounded-lg text-xs font-medium transition-colors disabled:opacity-50"
                    >
                      {wolBusy[pc_name]
                        ? <Loader2 className="w-3.5 h-3.5 animate-spin" />
                        : <Radio className="w-3.5 h-3.5" />}
                      <span>WoL {pc_name}</span>
                    </button>
                  ))}
                </div>
              </div>
            )}
            {remoteBusy && <Loader2 className="w-4 h-4 animate-spin text-slate-400 self-center" />}
          </div>
        </div>

        {/* Toolbar */}
        <div className="flex justify-between items-center">
          <p className="text-sm text-slate-500">Auto-refresh setiap 5 detik</p>
          <div className="flex space-x-3">
            <button
              onClick={() => fetchPcs()}
              className="px-4 py-2 bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 rounded-xl text-sm font-medium flex items-center space-x-2 transition-colors"
            >
              <RefreshCw className="w-4 h-4" />
              <span>Refresh</span>
            </button>
            <button
              onClick={() => setConfirmLogoutAll(true)}
              disabled={activeCount === 0}
              className="px-4 py-2 bg-red-50 border border-red-200 text-red-700 hover:bg-red-100 disabled:opacity-50 disabled:cursor-not-allowed rounded-xl text-sm font-medium flex items-center space-x-2 transition-colors"
            >
              <Lock className="w-4 h-4" />
              <span>Kunci Semua PC</span>
            </button>
          </div>
        </div>

        {/* Grid PC */}
        {pcsLoading ? (
          <div className="flex items-center justify-center py-24">
            <Loader2 className="w-8 h-8 text-blue-500 animate-spin" />
          </div>
        ) : (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
            {pcs.map((pc) => (
              <div
                key={pc.id}
                className={`relative overflow-hidden rounded-2xl border transition-all hover:shadow-lg group
                  ${pc.status === 'active'  ? 'border-blue-200 shadow-md bg-white'
                  : pc.status === 'locked'  ? 'border-slate-200 bg-slate-50'
                  :                          'border-red-100 bg-red-50 opacity-70'}`}
              >
                {/* Status bar atas */}
                <div className={`h-2 w-full ${pc.status === 'active' ? 'bg-blue-500' : pc.status === 'locked' ? 'bg-slate-300' : 'bg-red-400'}`} />

                <div className="p-5">
                  <div className="flex justify-between items-start mb-4">
                    <div className="flex items-center space-x-2">
                      <Monitor className={`w-5 h-5 ${pc.status === 'active' ? 'text-blue-500' : 'text-slate-400'}`} />
                      <div>
                        <h3 className="font-bold text-lg text-slate-800">{pc.id}</h3>
                        {(pc.label || pc.actual_pc_name !== pc.id) && (
                          <p className="text-xs text-slate-400">
                            {pc.label || 'Client aktif'}{pc.actual_pc_name && pc.actual_pc_name !== pc.id ? ` · ${pc.actual_pc_name}` : ''}
                          </p>
                        )}
                      </div>
                    </div>
                    <div className="flex items-center space-x-2">
                      {pc.status !== 'offline' && (
                        <button
                          onClick={() => setControlModalPc(pc)}
                          className="p-1.5 text-slate-400 hover:text-blue-600 hover:bg-blue-50 rounded-lg transition-colors opacity-0 group-hover:opacity-100"
                          title="Kontrol PC ini"
                        >
                          <Settings className="w-4 h-4" />
                        </button>
                      )}
                      <span className={`px-2.5 py-1 text-xs font-semibold rounded-full
                        ${pc.status === 'active'  ? 'bg-blue-100 text-blue-700'
                        : pc.status === 'locked'  ? 'bg-slate-200 text-slate-700'
                        :                          'bg-red-100 text-red-700'}`}>
                        {pc.status === 'active' ? 'Digunakan' : pc.status === 'locked' ? 'Terkunci' : 'Offline'}
                      </span>
                      {pc.is_unmapped && (
                        <span className="px-2.5 py-1 text-[11px] font-semibold rounded-full bg-amber-100 text-amber-700">
                          Belum Dipetakan
                        </span>
                      )}
                    </div>
                  </div>

                  {pc.status === 'active' ? (
                    <div className="space-y-3">
                      <div className="bg-blue-50/50 p-3 rounded-xl border border-blue-100">
                        <p className="text-sm font-semibold text-slate-800">{pc.student.name}</p>
                        <p className="text-xs text-slate-500">NIS: {pc.student.nis} · {pc.student.kelas}</p>
                      </div>
                      <div className="flex justify-between text-xs text-slate-500 px-1">
                        <span>Mulai: {pc.loginTime}</span>
                        <span className="font-medium text-blue-600">{pc.duration}</span>
                      </div>
                      {(pc.ip || pc.actual_pc_name) && (
                        <div className="text-xs text-slate-500 px-1">
                          {pc.actual_pc_name && pc.actual_pc_name !== pc.id && <span>Host: {pc.actual_pc_name}</span>}
                          {pc.actual_pc_name && pc.actual_pc_name !== pc.id && pc.ip && <span> · </span>}
                          {pc.ip && <span>IP: {pc.ip}</span>}
                        </div>
                      )}
                      <button
                        onClick={() => handleForceLogout(pc)}
                        className="w-full mt-2 py-2 bg-white hover:bg-red-50 text-red-600 border border-red-200 hover:border-red-300 rounded-lg text-sm font-medium transition-colors flex items-center justify-center space-x-2"
                      >
                        <LogOut className="w-4 h-4" />
                        <span>Paksa Keluar</span>
                      </button>
                    </div>
                  ) : (
                    <div className="flex flex-col items-center justify-center h-28 text-slate-400 space-y-2">
                      {pc.status === 'locked' ? (
                        <>
                          <ShieldCheck className="w-8 h-8 opacity-50" />
                          <p className="text-sm">Client Online, Menunggu Login</p>
                          {(pc.ip || pc.actual_pc_name) && (
                            <p className="text-[11px] text-slate-500 text-center px-3">
                              {pc.actual_pc_name && pc.actual_pc_name !== pc.id ? `${pc.actual_pc_name}` : ''}
                              {pc.actual_pc_name && pc.actual_pc_name !== pc.id && pc.ip ? ' · ' : ''}
                              {pc.ip || ''}
                            </p>
                          )}
                        </>
                      ) : (
                        <>
                          <Power className="w-8 h-8 opacity-50" />
                          <p className="text-sm">Komputer Dimatikan</p>
                          {(() => {
                            const m = clientMacs.find(e => e.pc_name === (pc.actual_pc_name || pc.id) || e.pc_name === pc.id);
                            if (!m) return null;
                            return (
                              <button
                                onClick={() => handleWakeOnLan(m.mac, pc.id)}
                                disabled={wolBusy[pc.id]}
                                className="mt-1 flex items-center space-x-1.5 px-3 py-1.5 bg-blue-50 hover:bg-blue-100 border border-blue-200 text-blue-700 rounded-lg text-xs font-medium transition-colors disabled:opacity-50"
                              >
                                {wolBusy[pc.id] ? <Loader2 className="w-3 h-3 animate-spin" /> : <Radio className="w-3 h-3" />}
                                <span>Wake-on-LAN</span>
                              </button>
                            );
                          })()}
                        </>
                      )}
                    </div>
                  )}

                  {pc.is_unmapped && (pc.status === 'active' || pc.status === 'locked') && (
                    <div className="mt-4 pt-4 border-t border-amber-100">
                      <p className="text-[11px] font-semibold uppercase tracking-wide text-amber-700 mb-2">
                        Mapping ke Slot Lab
                      </p>
                      <div className="flex gap-2">
                        <select
                          value={mappingSelections[pc.actual_pc_name || pc.id] || ''}
                          onChange={(e) => setMappingSelections((prev) => ({
                            ...prev,
                            [pc.actual_pc_name || pc.id]: e.target.value,
                          }))}
                          className="flex-1 px-3 py-2 text-xs border border-amber-200 rounded-lg bg-white text-slate-700 focus:outline-none focus:ring-2 focus:ring-amber-200"
                        >
                          <option value="">Pilih PC lab...</option>
                          {labPcOptions.map((option) => (
                            <option key={option.id} value={option.id}>
                              {option.id} - {option.label}
                            </option>
                          ))}
                        </select>
                        <button
                          onClick={() => handleAssignDeviceMapping(pc)}
                          disabled={mappingBusy[pc.actual_pc_name || pc.id]}
                          className="px-3 py-2 bg-amber-500 hover:bg-amber-600 disabled:opacity-60 text-white rounded-lg text-xs font-semibold transition-colors flex items-center space-x-1.5"
                        >
                          {mappingBusy[pc.actual_pc_name || pc.id]
                            ? <Loader2 className="w-3.5 h-3.5 animate-spin" />
                            : <Save className="w-3.5 h-3.5" />}
                          <span>Petakan</span>
                        </button>
                      </div>
                      <p className="text-[11px] text-slate-500 mt-2 break-all">
                        Host: {pc.actual_pc_name || pc.id}
                        {pc.mac ? ` · MAC: ${pc.mac}` : ''}
                      </p>
                    </div>
                  )}

                  {!pc.is_unmapped && (pc.binding_hostname || pc.binding_mac) && (
                    <div className="mt-4 pt-4 border-t border-slate-100">
                      <div className="flex items-center justify-between gap-3">
                        <div className="min-w-0">
                          <p className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">
                            Perangkat Terikat
                          </p>
                          <p className="text-xs text-slate-600 truncate mt-1">
                            {pc.binding_hostname || 'Hostname belum tersimpan'}
                          </p>
                          {pc.binding_mac && (
                            <p className="text-[11px] text-slate-400 truncate mt-0.5">{pc.binding_mac}</p>
                          )}
                        </div>
                        <button
                          onClick={() => handleClearDeviceMapping(pc)}
                          disabled={mappingBusy[pc.id]}
                          className="px-3 py-2 bg-white hover:bg-slate-50 border border-slate-200 text-slate-600 rounded-lg text-xs font-medium transition-colors flex items-center space-x-1.5 disabled:opacity-60"
                        >
                          {mappingBusy[pc.id]
                            ? <Loader2 className="w-3.5 h-3.5 animate-spin" />
                            : <X className="w-3.5 h-3.5" />}
                          <span>Lepas</span>
                        </button>
                      </div>
                    </div>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}

        {/* Confirm Kill All modal */}
        {confirmKillAll && (
          <div className="fixed inset-0 bg-slate-900/40 backdrop-blur-sm z-50 flex items-center justify-center p-4">
            <div className="bg-white rounded-2xl shadow-2xl max-w-sm w-full p-6 animate-in zoom-in-95 duration-300">
              <div className="flex items-center justify-center w-12 h-12 bg-red-100 rounded-full mx-auto mb-4">
                <PowerOff className="w-6 h-6 text-red-600" />
              </div>
              <h3 className="text-lg font-bold text-center text-slate-800 mb-2">
                {confirmKillAll === 'perm' ? 'Hentikan Permanen?' : 'Hentikan Sementara?'}
              </h3>
              <p className="text-sm text-slate-500 text-center mb-5">
                {confirmKillAll === 'perm'
                  ? 'Semua client ditutup dan TIDAK restart otomatis. Gunakan "Aktifkan Semua" untuk membuka kembali.'
                  : 'Semua client ditutup. Watchdog restart otomatis dalam ≤2 menit.'}
              </p>
              <div className="flex space-x-3">
                <button onClick={() => setConfirmKillAll(null)} className="flex-1 py-2.5 bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 rounded-xl font-medium transition-colors">Batal</button>
                <button onClick={() => handleKillAll(confirmKillAll === 'perm')} className="flex-1 py-2.5 bg-red-600 hover:bg-red-700 text-white rounded-xl font-medium transition-colors">
                  Ya, Hentikan
                </button>
              </div>
            </div>
          </div>
        )}

        {/* ── Device Claims (per-PC token) ── */}
        <div className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
          <div className="px-6 py-4 border-b border-slate-100 flex items-center justify-between">
            <div>
              <h3 className="font-bold text-slate-800 text-lg">Device Terdaftar</h3>
              <p className="text-sm text-slate-500 mt-0.5">PC yang sudah claim slot via token. Hapus claim jika PC reinstall atau ganti hardware.</p>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-xs bg-slate-100 text-slate-500 rounded-full px-3 py-1 font-medium">{deviceClaims.length} device</span>
              <button onClick={fetchDeviceClaims} disabled={claimsLoading} className="p-2 rounded-lg hover:bg-slate-100 text-slate-500" title="Refresh">
                {claimsLoading ? <Loader2 className="w-4 h-4 animate-spin" /> : <RefreshCw className="w-4 h-4" />}
              </button>
            </div>
          </div>
          {deviceClaims.length === 0 ? (
            <div className="px-6 py-8 text-center text-slate-400 text-sm">
              {claimsLoading ? 'Memuat…' : 'Belum ada device yang teregister.'}
            </div>
          ) : (
            <div className="divide-y divide-slate-100">
              {deviceClaims.map((claim) => (
                <div key={claim.pc_name} className="px-6 py-3 flex items-center gap-4 hover:bg-slate-50">
                  <div className="flex-1 min-w-0">
                    <p className="font-mono font-bold text-slate-800 text-sm">{claim.pc_name}</p>
                    <p className="text-xs text-slate-500 font-mono truncate">device: {claim.device_id}</p>
                    <p className="text-[11px] text-slate-400">expires: {claim.expires_at ? new Date(claim.expires_at).toLocaleString('id-ID') : '-'}</p>
                  </div>
                  <button
                    onClick={() => revokeDeviceClaim(claim.pc_name)}
                    className="px-3 py-1.5 bg-red-50 hover:bg-red-100 border border-red-200 text-red-700 rounded-lg text-xs font-medium flex items-center gap-1.5"
                  >
                    <Trash2 className="w-3.5 h-3.5" />
                    Hapus Claim
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    );
  };

  // ═══════════════════════════════════════════════════════════════════════
  // RENDER CONTROL
  // ═══════════════════════════════════════════════════════════════════════
  const renderControl = () => (
    <div className="space-y-6 animate-in fade-in duration-500 max-w-5xl">
      {ctrlLoading ? (
        <div className="flex items-center justify-center py-24"><Loader2 className="w-8 h-8 text-blue-500 animate-spin" /></div>
      ) : (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">

          {/* MODUL 1: Manajemen Daya Global */}
          <div className="bg-white rounded-2xl border border-slate-200 shadow-sm p-6">
            <div className="flex items-center space-x-3 mb-6">
              <div className="p-2 bg-red-100 text-red-600 rounded-lg"><Power className="w-5 h-5" /></div>
              <h3 className="text-lg font-bold text-slate-800">Manajemen Daya (Massal)</h3>
            </div>
            <p className="text-sm text-slate-500 mb-4">Kirimkan perintah ke seluruh PC yang sedang Online/Terkunci.</p>
            <div className="text-xs text-amber-600 bg-amber-50 border border-amber-200 rounded-xl px-3 py-2 mb-5">
              ⚠️ Fitur Shutdown & Sleep membutuhkan agent terpasang di setiap PC client.
            </div>
            <div className="grid grid-cols-2 gap-4">
              <button className="py-3 px-4 bg-red-50 hover:bg-red-100 border border-red-200 text-red-700 rounded-xl font-medium flex items-center justify-center space-x-2 transition-colors">
                <Power className="w-4 h-4" /><span>Shutdown Semua</span>
              </button>
              <button className="py-3 px-4 bg-indigo-50 hover:bg-indigo-100 border border-indigo-200 text-indigo-700 rounded-xl font-medium flex items-center justify-center space-x-2 transition-colors">
                <Moon className="w-4 h-4" /><span>Sleep Semua</span>
              </button>
              <button
                onClick={() => setConfirmLogoutAll(true)}
                className="col-span-2 py-3 px-4 bg-amber-50 hover:bg-amber-100 border border-amber-200 text-amber-700 rounded-xl font-medium flex items-center justify-center space-x-2 transition-colors"
              >
                <Lock className="w-4 h-4" /><span>Kunci Semua PC (Force Logout)</span>
              </button>
            </div>
          </div>

          {/* MODUL 2: Volume Master */}
          <div className="bg-white rounded-2xl border border-slate-200 shadow-sm p-6">
            <div className="flex items-center space-x-3 mb-6">
              <div className="p-2 bg-blue-100 text-blue-600 rounded-lg"><Volume2 className="w-5 h-5" /></div>
              <h3 className="text-lg font-bold text-slate-800">Pengaturan Volume (Master)</h3>
            </div>
            <p className="text-sm text-slate-500 mb-6">Atur batas volume maksimal untuk semua PC di lab.</p>
            <div className="space-y-6">
              <div className="flex items-center space-x-4">
                <button
                  onClick={() => setIsGlobalMuted(!isGlobalMuted)}
                  className={`p-3 rounded-xl transition-colors ${isGlobalMuted ? 'bg-red-100 text-red-600' : 'bg-slate-100 text-slate-600 hover:bg-slate-200'}`}
                >
                  {isGlobalMuted ? <VolumeX className="w-6 h-6" /> : <Volume2 className="w-6 h-6" />}
                </button>
                <div className="flex-1">
                  <div className="flex justify-between mb-2">
                    <span className="text-sm font-medium text-slate-700">Level Suara</span>
                    <span className="text-sm font-bold text-blue-600">{isGlobalMuted ? '0' : globalVolume}%</span>
                  </div>
                  <input
                    type="range" min="0" max="100" value={isGlobalMuted ? 0 : globalVolume}
                    onChange={(e) => { setGlobalVolume(Number(e.target.value)); if (Number(e.target.value) > 0) setIsGlobalMuted(false); }}
                    className="w-full h-2 bg-slate-200 rounded-lg appearance-none cursor-pointer accent-blue-600"
                  />
                </div>
              </div>
              <button
                onClick={() => saveSettings({ master_volume: String(globalVolume), master_muted: String(isGlobalMuted) })}
                disabled={ctrlSaving}
                className="w-full py-2.5 bg-blue-600 hover:bg-blue-700 disabled:bg-blue-400 text-white rounded-xl font-medium transition-colors flex items-center justify-center space-x-2"
              >
                {ctrlSaving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
                <span>Terapkan ke Semua PC</span>
              </button>
            </div>
          </div>

          {/* MODUL 3: Filter Akses Web */}
          <div className="bg-white rounded-2xl border border-slate-200 shadow-sm p-6 lg:col-span-2">
            <div className="flex items-center justify-between mb-4">
              <div className="flex items-center space-x-3">
                <div className="p-2 bg-emerald-100 text-emerald-600 rounded-lg"><Globe className="w-5 h-5" /></div>
                <div>
                  <h3 className="text-lg font-bold text-slate-800">Filter Akses Website</h3>
                  <p className="text-sm text-slate-500">Batasi atau blokir akses internet siswa ke situs tertentu.</p>
                </div>
              </div>
              {/* Toggle aktifkan filter */}
              <div className="flex items-center space-x-3">
                <span className="text-sm font-medium text-slate-600">Aktifkan Filter</span>
                <button
                  onClick={() => setWebFilterEnabled(!webFilterEnabled)}
                  className={`w-12 h-6 rounded-full relative transition-colors ${webFilterEnabled ? 'bg-emerald-500' : 'bg-slate-300'}`}
                >
                  <div className={`w-4 h-4 bg-white rounded-full absolute top-1 shadow-sm transition-all ${webFilterEnabled ? 'right-1' : 'left-1'}`} />
                </button>
              </div>
            </div>

            {/* Tab Whitelist / Blacklist */}
            <div className="flex space-x-2 mb-4 border-b border-slate-200 pb-4">
              {['blacklist', 'whitelist'].map(mode => (
                <button
                  key={mode}
                  onClick={() => setWebFilterMode(mode)}
                  className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors
                    ${webFilterMode === mode
                      ? mode === 'blacklist' ? 'bg-red-100 text-red-700' : 'bg-emerald-100 text-emerald-700'
                      : 'bg-slate-100 text-slate-600 hover:bg-slate-200'}`}
                >
                  {mode === 'blacklist' ? 'Daftar Hitam (Blacklist)' : 'Daftar Putih (Whitelist)'}
                </button>
              ))}
            </div>

            {/* Whitelist */}
            {webFilterMode === 'whitelist' ? (
              <>
                <form onSubmit={(e) => { e.preventDefault(); if (newWebsite && !whitelist.includes(newWebsite)) { setWhitelist([...whitelist, newWebsite]); setNewWebsite(''); } }} className="flex space-x-2 mb-4">
                  <input
                    value={newWebsite} onChange={e => setNewWebsite(e.target.value)}
                    placeholder="Contoh: wikipedia.org (Siswa HANYA bisa akses web ini)"
                    className="flex-1 px-4 py-2 border border-slate-300 rounded-xl focus:ring-2 focus:ring-emerald-500 outline-none text-sm"
                  />
                  <button type="submit" className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-white rounded-xl text-sm font-medium flex items-center space-x-2 transition-colors">
                    <Plus className="w-4 h-4" /><span>Tambah</span>
                  </button>
                </form>
                <div className="flex flex-wrap gap-2 min-h-[3rem]">
                  {whitelist.map((web, i) => (
                    <div key={i} className="flex items-center space-x-2 bg-emerald-50 border border-emerald-200 text-emerald-700 px-3 py-1.5 rounded-lg text-sm">
                      <span>{web}</span>
                      <button onClick={() => setWhitelist(whitelist.filter((_, j) => j !== i))} className="text-emerald-500 hover:text-emerald-700"><X className="w-4 h-4" /></button>
                    </div>
                  ))}
                  {whitelist.length === 0 && <p className="text-sm text-slate-400 italic">Belum ada website yang diizinkan.</p>}
                </div>
              </>
            ) : (
              <>
                <form onSubmit={(e) => { e.preventDefault(); if (newBlockedWeb && !blacklist.includes(newBlockedWeb)) { setBlacklist([...blacklist, newBlockedWeb]); setNewBlockedWeb(''); } }} className="flex space-x-2 mb-4">
                  <input
                    value={newBlockedWeb} onChange={e => setNewBlockedWeb(e.target.value)}
                    placeholder="Contoh: facebook.com (Siswa TIDAK BISA akses web ini)"
                    className="flex-1 px-4 py-2 border border-slate-300 rounded-xl focus:ring-2 focus:ring-red-500 outline-none text-sm"
                  />
                  <button type="submit" className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-white rounded-xl text-sm font-medium flex items-center space-x-2 transition-colors">
                    <Plus className="w-4 h-4" /><span>Blokir</span>
                  </button>
                </form>
                <div className="flex flex-wrap gap-2 min-h-[3rem]">
                  {blacklist.map((web, i) => (
                    <div key={i} className="flex items-center space-x-2 bg-red-50 border border-red-200 text-red-700 px-3 py-1.5 rounded-lg text-sm">
                      <span>{web}</span>
                      <button onClick={() => setBlacklist(blacklist.filter((_, j) => j !== i))} className="text-red-500 hover:text-red-700"><X className="w-4 h-4" /></button>
                    </div>
                  ))}
                  {blacklist.length === 0 && <p className="text-sm text-slate-400 italic">Belum ada website yang diblokir.</p>}
                </div>
              </>
            )}

            <div className="mt-4 flex justify-end">
              <button
                onClick={() => saveSettings()}
                disabled={ctrlSaving}
                className="px-5 py-2.5 bg-emerald-600 hover:bg-emerald-700 disabled:bg-emerald-400 text-white rounded-xl font-medium transition-colors flex items-center space-x-2 text-sm"
              >
                {ctrlSaving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
                <span>Simpan Pengaturan Filter</span>
              </button>
            </div>
          </div>

          {/* MODUL 4: Wallpaper */}
          <div className="bg-white rounded-2xl border border-slate-200 shadow-sm p-6 lg:col-span-2 flex flex-col sm:flex-row gap-6">
            <div className="flex-1">
              <div className="flex items-center space-x-3 mb-4">
                <div className="p-2 bg-purple-100 text-purple-600 rounded-lg"><ImageIcon className="w-5 h-5" /></div>
                <h3 className="text-lg font-bold text-slate-800">Personalisasi Wallpaper</h3>
              </div>
              <p className="text-sm text-slate-500 mb-4">Ganti gambar latar belakang desktop OS Windows maupun layar Kiosk siswa secara langsung.</p>
              <div className="space-y-3">
                <div className="space-y-1">
                  <label className="text-xs font-medium text-slate-700">Target Layar</label>
                  <select
                    value={wallpaperTarget} onChange={e => setWallpaperTarget(e.target.value)}
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl focus:ring-2 focus:ring-purple-500 outline-none text-sm"
                  >
                    <option value="both">Keduanya (Desktop OS & Layar Terkunci)</option>
                    <option value="desktop">Hanya Desktop OS Windows</option>
                    <option value="kiosk">Hanya Layar Terkunci (Kiosk)</option>
                  </select>
                </div>
                <div className="space-y-1">
                  <label className="text-xs font-medium text-slate-700">URL Gambar</label>
                  <input
                    type="text" value={wallpaperUrl} onChange={e => setWallpaperUrl(e.target.value)}
                    placeholder="Masukkan URL gambar..."
                    className="w-full px-3 py-2 border border-slate-300 rounded-xl focus:ring-2 focus:ring-purple-500 outline-none text-sm"
                  />
                </div>
                <button
                  onClick={() => saveSettings({ wallpaper_url: wallpaperUrl, wallpaper_target: wallpaperTarget })}
                  disabled={ctrlSaving}
                  className="px-4 py-2 bg-purple-600 hover:bg-purple-700 disabled:bg-purple-400 text-white rounded-xl text-sm font-medium transition-colors flex items-center space-x-2"
                >
                  {ctrlSaving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
                  <span>Terapkan Wallpaper</span>
                </button>
              </div>
            </div>
            <div className="w-full sm:w-64 h-40 bg-slate-100 rounded-xl border border-slate-300 overflow-hidden relative shadow-inner">
              {wallpaperUrl && <img src={wallpaperUrl} alt="Preview" className="w-full h-full object-cover" onError={e => { e.target.style.display = 'none'; }} />}
              <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
                <span className="bg-black/50 text-white text-xs px-2 py-1 rounded backdrop-blur-sm">Pratinjau Layar</span>
              </div>
            </div>
          </div>

        </div>
      )}
    </div>
  );

  // ═══════════════════════════════════════════════════════════════════════
  // RENDER STUDENTS
  // ═══════════════════════════════════════════════════════════════════════
  const renderStudents = () => (
    <div className="bg-white rounded-2xl border border-slate-200 shadow-sm overflow-hidden animate-in fade-in duration-500">
      <div className="p-6 border-b border-slate-200 flex flex-col sm:flex-row justify-between items-center gap-4">
        <div className="relative w-full sm:w-72">
          <Search className="w-5 h-5 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
          <input
            type="text" value={stuSearch} onChange={e => setStuSearch(e.target.value)}
            placeholder="Cari NIS atau Nama..."
            className="w-full pl-10 pr-4 py-2 border border-slate-300 rounded-lg focus:ring-2 focus:ring-blue-500 outline-none"
          />
        </div>
        <button
          onClick={() => setStuModal('add')}
          className="w-full sm:w-auto px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center justify-center space-x-2 font-medium transition-colors"
        >
          <Plus className="w-4 h-4" /><span>Tambah Siswa</span>
        </button>
      </div>
      {stuLoading ? (
        <div className="flex items-center justify-center py-24"><Loader2 className="w-8 h-8 text-blue-500 animate-spin" /></div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-left border-collapse">
            <thead>
              <tr className="bg-slate-50 text-slate-500 text-sm border-b border-slate-200">
                <th className="p-4 font-medium">NIS</th>
                <th className="p-4 font-medium">Nama Lengkap</th>
                <th className="p-4 font-medium">Kelas</th>
                <th className="p-4 font-medium">Status</th>
                <th className="p-4 font-medium text-right">Aksi</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-200">
              {filteredStudents.length === 0 ? (
                <tr><td colSpan={5} className="p-8 text-center text-slate-400 text-sm">Tidak ada data siswa.</td></tr>
              ) : filteredStudents.map((s) => (
                <tr key={s.id} className="hover:bg-slate-50">
                  <td className="p-4 text-sm font-medium text-slate-800">{s.nis}</td>
                  <td className="p-4 text-sm text-slate-600">{s.nama_lengkap}</td>
                  <td className="p-4 text-sm text-slate-600">{s.kelas || '-'}</td>
                  <td className="p-4">
                    <span className={`px-2.5 py-1 text-xs font-medium rounded-full ${s.is_active ? 'bg-green-100 text-green-700' : 'bg-slate-100 text-slate-600'}`}>
                      {s.is_active ? 'Aktif' : 'Nonaktif'}
                    </span>
                  </td>
                  <td className="p-4 flex justify-end space-x-2">
                    <button onClick={() => setStuModal(s)} className="p-1.5 text-blue-600 hover:bg-blue-50 rounded-lg transition-colors"><Edit className="w-4 h-4" /></button>
                    <button onClick={() => setDeleteTarget(s)}  className="p-1.5 text-red-600 hover:bg-red-50 rounded-lg transition-colors"><Trash2 className="w-4 h-4" /></button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="px-4 py-3 border-t border-slate-100 text-xs text-slate-400">
            {filteredStudents.length} dari {students.length} siswa
          </div>
        </div>
      )}
    </div>
  );

  // ═══════════════════════════════════════════════════════════════════════
  // RENDER HISTORY
  // ═══════════════════════════════════════════════════════════════════════
  const totalPages = Math.ceil(histTotal / HIST_LIMIT);

  const renderHistory = () => (
    <div className="bg-white rounded-2xl border border-slate-200 shadow-sm overflow-hidden animate-in fade-in duration-500">
      <div className="p-6 border-b border-slate-200 flex flex-col sm:flex-row justify-between items-center gap-4">
        <h3 className="text-lg font-semibold text-slate-800">Riwayat Penggunaan Lab</h3>
        <div className="flex space-x-2">
          <input
            type="date"
            value={histDate} onChange={e => setHistDate(e.target.value)}
            className="px-3 py-2 border border-slate-300 rounded-lg text-sm text-slate-600 focus:outline-none focus:border-blue-500"
          />
          <button
            onClick={() => fetchHistory(1)}
            disabled={histLoading}
            className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg text-sm font-medium transition-colors flex items-center space-x-2"
          >
            {histLoading ? <Loader2 className="w-4 h-4 animate-spin" /> : <Search className="w-4 h-4" />}
            <span>Cari</span>
          </button>
          {histDate && (
            <button onClick={() => { setHistDate(''); setTimeout(() => fetchHistory(1), 0); }} className="px-3 py-2 border border-slate-300 text-slate-600 hover:bg-slate-50 rounded-lg text-sm transition-colors">
              <X className="w-4 h-4" />
            </button>
          )}
        </div>
      </div>
      {histLoading ? (
        <div className="flex items-center justify-center py-24"><Loader2 className="w-8 h-8 text-blue-500 animate-spin" /></div>
      ) : (
        <>
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="bg-slate-50 text-slate-500 text-xs uppercase tracking-wider border-b border-slate-200">
                  <th className="p-4 font-medium">Tanggal</th>
                  <th className="p-4 font-medium">PC</th>
                  <th className="p-4 font-medium">Siswa</th>
                  <th className="p-4 font-medium">Waktu (Masuk – Keluar)</th>
                  <th className="p-4 font-medium">Status Akhir</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200">
                {history.length === 0 ? (
                  <tr><td colSpan={5} className="p-8 text-center text-slate-400 text-sm">Tidak ada riwayat sesi{histDate ? ` pada tanggal ${histDate}` : ''}.</td></tr>
                ) : history.map((h) => (
                  <tr key={h.id} className="hover:bg-slate-50">
                    <td className="p-4 text-sm text-slate-600 whitespace-nowrap">{h.date}</td>
                    <td className="p-4 text-sm font-medium text-slate-800">{h.pc}</td>
                    <td className="p-4">
                      <p className="text-sm font-medium text-slate-800">{h.name}</p>
                      <p className="text-xs text-slate-500">{h.nis} · {h.kelas}</p>
                    </td>
                    <td className="p-4">
                      <p className="text-sm text-slate-600">{h.login} – {h.logout}</p>
                      <p className="text-xs text-slate-400">Durasi: {h.duration}</p>
                    </td>
                    <td className="p-4">
                      <span className={`px-2.5 py-1 text-xs font-medium rounded-full
                        ${h.status === 'active'        ? 'bg-blue-100 text-blue-700'
                        : h.type.includes('Normal')    ? 'bg-green-100 text-green-700'
                        :                               'bg-red-100 text-red-700'}`}>
                        {h.type}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          {totalPages > 1 && (
            <div className="p-4 border-t border-slate-100 flex items-center justify-between text-sm text-slate-600">
              <span>{histTotal} total sesi</span>
              <div className="flex items-center space-x-2">
                <button disabled={histPage === 1} onClick={() => fetchHistory(histPage - 1)} className="p-1.5 rounded-lg hover:bg-slate-100 disabled:opacity-40 disabled:cursor-not-allowed">
                  <ChevronLeft className="w-4 h-4" />
                </button>
                <span className="font-medium">Hal {histPage} / {totalPages}</span>
                <button disabled={histPage === totalPages} onClick={() => fetchHistory(histPage + 1)} className="p-1.5 rounded-lg hover:bg-slate-100 disabled:opacity-40 disabled:cursor-not-allowed">
                  <ChevronRight className="w-4 h-4" />
                </button>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  );

  // ═══════════════════════════════════════════════════════════════════════
  // MAIN RENDER
  // ═══════════════════════════════════════════════════════════════════════
  const getScreenForPc = (pc) => {
    const pcId = String(pc?.id || '').toUpperCase();
    const actualPc = String(pc?.actual_pc_name || '').toUpperCase();
    return screens.find((screen) => {
      const screenPc = String(screen.pc_name || '').toUpperCase();
      return screenPc === pcId || screenPc === actualPc;
    });
  };

  const getPcStudentName = (pc) => pc?.student?.name || pc?.student?.nama_lengkap || 'Belum ada siswa';

  const activePcs = pcs.filter((pc) => pc.status === 'active');
  const lockedPcs = pcs.filter((pc) => pc.status === 'locked');
  const onlineCount = pcs.filter((pc) => pc.status !== 'offline').length;
  const offlineCount = pcs.filter((pc) => pc.status === 'offline').length;
  const activeClassName = 'Lab Aktif';

  const activityFeed = [
    ...activePcs.slice(0, 5).map((pc) => ({
      icon: Activity,
      time: new Date(pc.last_seen || Date.now()).toLocaleTimeString('id-ID', { hour: '2-digit', minute: '2-digit' }),
      title: getPcStudentName(pc),
      detail: `${pc.id} sedang aktif`,
    })),
    ...lockedPcs.slice(0, 2).map((pc) => ({
      icon: Lock,
      time: new Date(pc.last_seen || Date.now()).toLocaleTimeString('id-ID', { hour: '2-digit', minute: '2-digit' }),
      title: pc.id,
      detail: 'Menunggu login siswa',
    })),
  ].slice(0, 7);

  const classroomRows = [
    { label: 'Lab Komputer', count: pcs.length, active: true },
  ];

  const renderPcPreview = (pc, index) => {
    const screen = getScreenForPc(pc);
    const isOffline = pc.status === 'offline';

    if (isOffline) {
      return (
        <div className="h-32 bg-slate-800 text-slate-400 flex flex-col items-center justify-center gap-2">
          <Monitor className="w-8 h-8 opacity-60" />
          <span className="text-[10px] font-semibold tracking-[0.2em]">OFFLINE</span>
        </div>
      );
    }

    return (
      <div className="h-32 bg-[#0837d8] p-1.5">
        <div className="h-full rounded-sm bg-white overflow-hidden border border-blue-950/20">
          <div className={`h-5 flex items-center justify-between px-2 text-[9px] text-white ${pc.status === 'active' ? 'bg-emerald-600' : 'bg-slate-500'}`}>
            <span className="truncate">{screen ? 'Live Screen' : (pc.status === 'active' ? 'Online' : 'Idle')}</span>
            <span>•••</span>
          </div>
          {screen?.image ? (
            <img src={screen.image} alt={pc.id} className="w-full h-[calc(100%-1.25rem)] object-cover" />
          ) : (
            <div className="h-[calc(100%-1.25rem)] bg-slate-50 p-3">
              <div className="h-2 bg-blue-500/70 rounded mb-3 w-1/3" />
              <div className="space-y-2">
                <div className="h-1.5 bg-slate-300 rounded w-full" />
                <div className="h-1.5 bg-slate-200 rounded w-4/5" />
                <div className="h-1.5 bg-slate-200 rounded w-2/3" />
              </div>
              {index % 4 === 1 && <div className="mt-5 h-10 w-20 bg-lime-300 mx-auto" />}
            </div>
          )}
        </div>
      </div>
    );
  };

  const renderLabSidebar = () => (
    <aside className="w-[190px] shrink-0 bg-[#f5f8fc] border-r border-slate-200 flex flex-col text-[11px]">
      <div className="px-3 py-2.5 border-b border-slate-200">
        <p className="font-bold text-slate-500 uppercase mb-2">Kelas Aktif</p>
        <div className="space-y-1">
          {classroomRows.map((row) => (
            <button key={row.label} className={`w-full flex items-center justify-between px-2 py-1.5 rounded ${row.active ? 'bg-blue-100 text-blue-700' : 'text-slate-600 hover:bg-white'}`}>
              <span className="flex items-center gap-1.5"><Users className="w-3.5 h-3.5" />{row.label}</span>
              <span className="rounded-full bg-white/80 px-1.5 text-[10px]">{row.count}</span>
            </button>
          ))}
        </div>
      </div>

      <div className="px-3 py-2.5 border-b border-slate-200">
        <p className="font-bold text-slate-500 uppercase mb-2">Tampilan</p>
        <div className="space-y-1">
          {[
            { id: 'monitoring', label: 'Grid Thumbnail', icon: LayoutGrid },
            { id: 'screens', label: 'Daftar Detail', icon: List },
            { id: 'history', label: 'Layout Lab', icon: Monitor },
          ].map(({ id, label, icon: Icon }) => (
            <button key={id} onClick={() => setActiveTab(id)} className={`w-full flex items-center gap-2 px-2 py-1.5 rounded text-left ${activeTab === id ? 'bg-blue-100 text-blue-700' : 'text-slate-600 hover:bg-white'}`}>
              <Icon className="w-3.5 h-3.5" /> {label}
            </button>
          ))}
        </div>
      </div>

      <div className="px-3 py-2.5">
        <p className="font-bold text-slate-500 uppercase mb-2">Grup</p>
        <div className="space-y-1">
          {[
            ['Semua PC', pcs.length],
            ['Aktif (login)', activePcs.length],
            ['Idle (belum login)', lockedPcs.length],
            ['Offline', offlineCount],
          ].map(([label, count]) => (
            <button key={label} className="w-full flex items-center justify-between px-2 py-1.5 rounded text-slate-600 hover:bg-white">
              <span className="flex items-center gap-1.5"><FolderOpen className="w-3.5 h-3.5" />{label}</span>
              <span className="rounded-full bg-slate-200 px-1.5 text-[10px]">{count}</span>
            </button>
          ))}
        </div>
      </div>

      <div className="mt-auto p-3 border-t border-slate-200">
        <p className="font-bold text-slate-500 uppercase mb-2">Status</p>
        <div className="space-y-1 text-[11px] text-slate-600">
          <div className="flex justify-between"><span>Online</span><span className="font-semibold text-emerald-600">{onlineCount}</span></div>
          <div className="flex justify-between"><span>Offline</span><span className="font-semibold text-slate-500">{offlineCount}</span></div>
        </div>
      </div>
    </aside>
  );

  const renderActivityRail = () => (
    <aside className="w-[270px] shrink-0 border-l border-slate-200 bg-white">
      <div className="h-full flex flex-col">
        <div className="px-4 py-3 border-b border-slate-200 flex items-center justify-between">
          <p className="font-bold text-slate-700 text-xs uppercase tracking-wide">Aktivitas Terkini</p>
          <button className="text-slate-400 hover:text-slate-600"><X className="w-3.5 h-3.5" /></button>
        </div>
        <div className="divide-y divide-slate-100 overflow-y-auto">
          {(activityFeed.length ? activityFeed : [{ icon: CheckCircle2, time: '14:15', title: 'Server aktif', detail: 'Menunggu aktivitas siswa' }]).map((item, index) => {
            const Icon = item.icon;
            return (
              <div key={`${item.time}-${index}`} className="px-4 py-3 flex gap-3">
                <Icon className="w-4 h-4 text-blue-500 mt-1" />
                <div className="min-w-0">
                  <p className="text-[10px] text-slate-400 font-mono">{item.time}</p>
                  <p className="text-xs font-semibold text-slate-700 truncate">{item.title}</p>
                  <p className="text-[11px] text-slate-500 truncate">{item.detail}</p>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </aside>
  );

  const renderLabMonitorView = () => (
    <div className="h-full flex">
      <div className="flex-1 min-w-0 overflow-y-auto p-4 bg-[#eef3f9]">
        <div className="flex items-center justify-between mb-3">
          <div className="flex items-center gap-2">
            <button onClick={() => fetchPcs()} className="px-3 py-1.5 rounded border border-slate-300 bg-white text-xs font-semibold text-slate-600 flex items-center gap-1.5">
              <RefreshCw className="w-3.5 h-3.5" /> Refresh
            </button>
            <button onClick={() => setConfirmLogoutAll(true)} className="px-3 py-1.5 rounded bg-blue-600 text-white text-xs font-semibold flex items-center gap-1.5">
              <Lock className="w-3.5 h-3.5" /> Kunci Semua
            </button>
          </div>
          <div className="relative w-56">
            <Search className="w-3.5 h-3.5 text-slate-400 absolute left-2 top-1/2 -translate-y-1/2" />
            <input className="w-full pl-7 pr-3 py-1.5 text-xs rounded border border-slate-300 bg-white outline-none focus:border-blue-500" placeholder="Cari siswa atau PC..." />
          </div>
        </div>

        {pcsLoading ? (
          <div className="h-80 flex items-center justify-center text-blue-600"><Loader2 className="w-8 h-8 animate-spin" /></div>
        ) : (
          <div className="grid grid-cols-2 xl:grid-cols-4 2xl:grid-cols-5 gap-3">
            {pcs.map((pc, index) => (
              <button key={pc.id} onClick={() => pc.status !== 'offline' && setControlModalPc(pc)} className={`text-left bg-white border rounded overflow-hidden shadow-sm hover:shadow-md transition ${pc.status === 'active' ? 'border-blue-500' : pc.status === 'locked' ? 'border-amber-300' : 'border-slate-300 opacity-80'}`}>
                {renderPcPreview(pc, index)}
                <div className="p-2">
                  <div className="flex items-center justify-between">
                    <p className="font-bold text-[11px] text-slate-800">{pc.id}</p>
                    <span className={`text-[9px] rounded-full px-1.5 py-0.5 ${pc.status === 'active' ? 'bg-emerald-50 text-emerald-600' : pc.status === 'locked' ? 'bg-amber-50 text-amber-600' : 'bg-slate-100 text-slate-500'}`}>
                      {pc.status === 'active' ? 'Online' : pc.status === 'locked' ? 'Idle' : 'Offline'}
                    </span>
                  </div>
                  <p className="text-[11px] font-semibold text-slate-700 truncate mt-1">{getPcStudentName(pc)}</p>
                  <p className="text-[10px] text-slate-500 truncate">{pc.student?.kelas || pc.duration || '-'}</p>
                </div>
              </button>
            ))}
          </div>
        )}
      </div>
      {renderActivityRail()}
    </div>
  );

  const renderBroadcastStudio = () => (
    <div className="h-full grid grid-cols-[minmax(0,1fr)_260px] gap-4 p-4 bg-[#eef3f9] overflow-y-auto">
      <div className="space-y-3">
        <ScreenShareAdmin socket={realtimeSocketRef.current} />
      </div>
      <div className="space-y-3">
        <div className="bg-white border border-slate-200 rounded p-3">
          <div className="flex justify-between items-center mb-3">
            <p className="text-xs font-bold uppercase text-slate-700">Siswa Online ({onlineCount})</p>
          </div>
          {pcs.length === 0 ? (
            <p className="text-[11px] text-slate-400 text-center py-4">Belum ada siswa terhubung</p>
          ) : (
            <div className="grid grid-cols-2 gap-2">
              {pcs.filter((pc) => pc.status !== 'offline').slice(0, 24).map((pc) => (
                <div key={pc.id} className="border border-slate-100 rounded p-2">
                  <p className="text-[11px] font-semibold truncate">{getPcStudentName(pc)}</p>
                  <p className="text-[10px] text-slate-400">{pc.id}</p>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );

  // eslint-disable-next-line no-unused-vars
  const _UNUSED_QUIZ_STUDIO = () => (
    <div className="h-full grid grid-cols-[220px_minmax(0,1fr)_230px] gap-4 p-4 bg-[#eef3f9] overflow-y-auto">
      <div className="bg-white border border-slate-200 rounded p-3">
        <p className="text-xs font-bold text-slate-600 uppercase mb-3">Pertanyaan</p>
        {['Tag semantic untuk header halaman', 'Struktur semantic konten', 'Aksesibilitas main landmark', 'True/False: div selalu salah'].map((q, i) => (
          <button key={q} className={`w-full text-left px-2 py-2 rounded mb-1 text-[11px] ${i === 0 ? 'bg-blue-100 text-blue-700' : 'hover:bg-slate-50 text-slate-600'}`}>
            {i + 1}. {q}
          </button>
        ))}
      </div>
      <div className="space-y-4">
        <div className="bg-blue-700 text-white rounded p-8 text-center">
          <p className="text-xs uppercase opacity-80 mb-4">Soal 01 / 5 - Sedang berlangsung</p>
          <h2 className="text-xl font-bold mb-8">Tag semantik HTML5 manakah untuk bagian header halaman?</h2>
          <div className="grid grid-cols-3 gap-6 max-w-lg mx-auto">
            <div><p className="text-3xl font-bold">00:42</p><p className="text-xs opacity-80">tersisa</p></div>
            <div><p className="text-3xl font-bold">28/30</p><p className="text-xs opacity-80">menjawab</p></div>
            <div><p className="text-3xl font-bold">78%</p><p className="text-xs opacity-80">benar</p></div>
          </div>
        </div>
        <div className="bg-white border border-slate-200 rounded p-4">
          <p className="text-xs font-bold uppercase text-slate-700 mb-3">Distribusi Jawaban</p>
          {[['A. <header>', 78, true], ['B. <section>', 14], ['C. <top>', 4], ['D. <div class=\"header\">', 4]].map(([label, percent, correct]) => (
            <div key={label} className="mb-3">
              <div className="flex justify-between text-[11px] mb-1"><span>{label}</span><span>{percent}%</span></div>
              <div className="h-4 bg-slate-100 rounded overflow-hidden">
                <div className={`h-full ${correct ? 'bg-emerald-500' : 'bg-slate-400'}`} style={{ width: `${percent}%` }} />
              </div>
            </div>
          ))}
        </div>
      </div>
      <div className="space-y-3">
        <div className="bg-white border border-slate-200 rounded p-3">
          <p className="text-xs font-bold uppercase text-slate-700 mb-2">Status Siswa - Live</p>
          <div className="grid grid-cols-2 gap-1">
            {pcs.slice(0, 18).map((pc, i) => (
              <div key={pc.id} className={`text-[10px] rounded px-2 py-1 ${i % 5 === 0 ? 'bg-amber-50 text-amber-700' : 'bg-emerald-50 text-emerald-700'}`}>
                {getPcStudentName(pc).split(' ')[0]}
              </div>
            ))}
          </div>
        </div>
        <div className="bg-white border border-slate-200 rounded p-3">
          <p className="text-xs font-bold uppercase text-slate-700 mb-2">Leaderboard Sementara</p>
          {activePcs.slice(0, 5).map((pc, i) => (
            <div key={pc.id} className="flex justify-between text-[11px] py-1">
              <span>{i + 1}. {getPcStudentName(pc)}</span><span className="text-blue-600 font-bold">{256 - i * 14}</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  );

  // eslint-disable-next-line no-unused-vars
  const _UNUSED_FILE_STUDIO = () => (
    <div className="h-full grid grid-cols-[260px_minmax(0,1fr)] gap-4 p-4 bg-[#eef3f9] overflow-y-auto">
      <div className="bg-white border border-slate-200 rounded p-3">
        <div className="flex items-center justify-between mb-3">
          <p className="text-xs font-bold uppercase text-slate-700">File Kelas</p>
          <button className="px-2 py-1 bg-blue-600 text-white rounded text-[10px] flex items-center gap-1"><Upload className="w-3 h-3" />Upload</button>
        </div>
        {['materi-pertemuan-8.pdf', 'tugas-html-template.zip', 'semantic-html.link', 'video-demo.mp4', 'quiz-html.docx'].map((file, i) => (
          <button key={file} className="w-full flex items-center gap-2 px-2 py-2 rounded hover:bg-slate-50 text-left">
            <FileText className="w-4 h-4 text-blue-500" />
            <div className="min-w-0 flex-1">
              <p className="text-[11px] font-semibold text-slate-700 truncate">{file}</p>
              <p className="text-[10px] text-slate-400">{i + 1}.2 MB - 14:2{i}</p>
            </div>
            <span className={`text-[9px] rounded px-1.5 py-0.5 ${i === 2 ? 'bg-blue-50 text-blue-600' : 'bg-emerald-50 text-emerald-600'}`}>{i === 2 ? 'Sedang dikirim' : 'Terkirim'}</span>
          </button>
        ))}
      </div>
      <div className="space-y-4">
        <div className="bg-white border border-slate-200 rounded p-4">
          <div className="flex justify-between mb-4">
            <div>
              <p className="text-xs text-slate-500 font-semibold uppercase">Pengiriman: Contoh Kode HTML</p>
              <h3 className="font-bold text-slate-900">Distribusi File & Pengumpulan Tugas</h3>
            </div>
            <button className="text-xs text-blue-600 font-semibold">Selesai Berbagi</button>
          </div>
          <div className="grid grid-cols-4 gap-4 text-xs mb-4">
            <div><p className="text-slate-400">File</p><p className="font-semibold">semantic-html.zip</p></div>
            <div><p className="text-slate-400">Tujuan</p><p className="font-semibold">Semua Siswa</p></div>
            <div><p className="text-slate-400">Mode</p><p className="font-semibold">Salin ke Buku Otomatis</p></div>
            <div><p className="text-slate-400">Akses</p><p className="font-semibold">Read only - 1 jam</p></div>
          </div>
          <div className="flex items-center gap-3">
            <div className="flex-1 h-2 bg-slate-100 rounded"><div className="h-2 bg-blue-600 rounded w-[93%]" /></div>
            <span className="text-xs font-bold text-blue-700">93%</span>
          </div>
        </div>
        <div className="bg-white border border-slate-200 rounded p-4">
          <p className="text-xs font-bold uppercase text-slate-700 mb-3">Status Per Siswa</p>
          <div className="grid grid-cols-3 gap-2">
            {pcs.slice(0, 24).map((pc, i) => (
              <div key={pc.id} className="flex items-center justify-between border border-slate-100 rounded px-2 py-1.5 text-[11px]">
                <span className="truncate">{getPcStudentName(pc)}</span>
                <span className={`${i % 7 === 0 ? 'text-amber-600 bg-amber-50' : i % 11 === 0 ? 'text-red-600 bg-red-50' : 'text-emerald-600 bg-emerald-50'} rounded px-1.5 py-0.5`}>
                  {i % 7 === 0 ? 'Menunggu' : i % 11 === 0 ? 'Gagal' : 'Diterima'}
                </span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );

  // eslint-disable-next-line no-unused-vars
  const _UNUSED_SETTINGS_STUDIO = () => (
    <div className="h-full p-5 bg-[#eef3f9] overflow-y-auto">
      <div className="max-w-3xl">
        <h3 className="text-lg font-bold text-slate-900">Konfigurasi Lab</h3>
        <p className="text-xs text-slate-500 mb-4">Identitas lab, layout fisik, dan default sesi.</p>
        <div className="space-y-3">
          <div className="bg-white border border-slate-200 rounded">
            <div className="px-4 py-2 border-b border-slate-100 text-xs font-bold uppercase text-slate-600">Identitas Lab</div>
            <div className="grid grid-cols-2 gap-4 p-4 text-xs">
              <label>Nama Lab<input className="mt-1 w-full border border-slate-300 rounded px-2 py-1.5" defaultValue="Lab Komputer 1" /></label>
              <label>Lokasi<input className="mt-1 w-full border border-slate-300 rounded px-2 py-1.5" defaultValue="Lantai 2 - Gedung B" /></label>
              <label>Kapasitas<input className="mt-1 w-full border border-slate-300 rounded px-2 py-1.5" defaultValue={`${pcs.length || 32} unit`} /></label>
              <label>Penanggung Jawab<input className="mt-1 w-full border border-slate-300 rounded px-2 py-1.5" defaultValue="Pak Rudi" /></label>
            </div>
          </div>
          <div className="bg-white border border-slate-200 rounded">
            <div className="px-4 py-2 border-b border-slate-100 text-xs font-bold uppercase text-slate-600">Layout Komputer</div>
            <div className="grid grid-cols-3 gap-4 p-4 text-xs">
              <label>Baris<input className="mt-1 w-full border border-slate-300 rounded px-2 py-1.5" defaultValue="5" /></label>
              <label>Kolom<input className="mt-1 w-full border border-slate-300 rounded px-2 py-1.5" defaultValue="6" /></label>
              <label>Penomoran<select className="mt-1 w-full border border-slate-300 rounded px-2 py-1.5"><option>Kiri ke kanan - atas ke bawah</option></select></label>
            </div>
          </div>
          <div className="bg-white border border-slate-200 rounded">
            <div className="px-4 py-2 border-b border-slate-100 text-xs font-bold uppercase text-slate-600">Default Sesi</div>
            <div className="p-4 space-y-3 text-xs">
              {[
                ['Auto-lock saat tangan diangkat', true],
                ['Rekam aktivitas siswa otomatis', webFilterEnabled],
                ['Kunci semua PC saat mulai sesi', false],
                ['Kirim notifikasi ke siswa saat dipantau', true],
              ].map(([label, enabled]) => (
                <div key={label} className="flex items-center justify-between">
                  <span>{label}</span>
                  <button className={`w-9 h-5 rounded-full relative ${enabled ? 'bg-blue-600' : 'bg-slate-300'}`}>
                    <span className={`absolute top-1 h-3 w-3 rounded-full bg-white ${enabled ? 'right-1' : 'left-1'}`} />
                  </button>
                </div>
              ))}
              <div className="pt-3 border-t border-slate-100">
                <div className="flex justify-between text-xs mb-1"><span>Volume default</span><span>{globalVolume}%</span></div>
                <input type="range" min="0" max="100" value={globalVolume} onChange={(e) => setGlobalVolume(Number(e.target.value))} className="w-full accent-blue-600" />
              </div>
            </div>
          </div>
          <button onClick={() => saveSettings()} disabled={ctrlSaving} className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded text-xs font-bold flex items-center gap-2">
            {ctrlSaving ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />} Simpan Konfigurasi
          </button>
        </div>
      </div>
    </div>
  );

  const renderWorkspaceContent = () => {
    if (activeTab === 'monitoring') return renderLabMonitorView();
    if (activeTab === 'screenshare') return renderBroadcastStudio();
    if (activeTab === 'checks')   return <div className="p-4 bg-[#eef3f9] h-full overflow-y-auto">{renderChecks()}</div>;
    if (activeTab === 'activities') return <div className="p-4 bg-[#eef3f9] h-full overflow-y-auto"><ActivityMonitor serverUrl={API} socket={realtimeSocketRef.current} /></div>;
    if (activeTab === 'control')  return <div className="p-4 bg-[#eef3f9] h-full overflow-y-auto">{renderControl()}</div>;
    if (activeTab === 'screens')  return <div className="p-4 bg-[#eef3f9] h-full overflow-y-auto">{renderScreens()}</div>;
    if (activeTab === 'students') return <div className="p-4 bg-[#eef3f9] h-full overflow-y-auto">{renderStudents()}</div>;
    if (activeTab === 'history')  return <div className="p-4 bg-[#eef3f9] h-full overflow-y-auto">{renderHistory()}</div>;
    if (activeTab === 'server')   return <div className="p-4 bg-[#eef3f9] h-full overflow-y-auto">{renderServer()}</div>;
    return renderLabMonitorView();
  };

  if (authLoading) {
    return (
      <div className="min-h-screen bg-slate-100 flex items-center justify-center">
        <div className="bg-white border border-slate-200 rounded-2xl px-8 py-6 shadow-sm flex items-center gap-3 text-slate-600">
          <Loader2 className="w-5 h-5 animate-spin text-blue-600" />
          <span>Memverifikasi akses admin...</span>
        </div>
      </div>
    );
  }

  if (!authReady) {
    return (
      <div className="min-h-screen bg-slate-100 flex items-center justify-center p-4">
        <form onSubmit={handleAdminLogin} className="w-full max-w-md bg-white border border-slate-200 rounded-2xl shadow-sm p-8 space-y-5">
          <div className="text-center">
            <h1 className="text-2xl font-bold text-slate-800">Login Admin Lab</h1>
            <p className="text-sm text-slate-500 mt-1">Masukkan password admin untuk membuka dashboard.</p>
          </div>
          {authError && (
            <div className="bg-red-50 border border-red-200 text-red-700 text-sm rounded-xl px-4 py-3">
              {authError}
            </div>
          )}
          <div className="space-y-2">
            <label className="text-sm text-slate-600 font-medium">Password Admin</label>
            <input
              type="password"
              value={adminPassword}
              onChange={(e) => setAdminPassword(e.target.value)}
              className="w-full px-4 py-3 border border-slate-300 rounded-xl focus:outline-none focus:border-blue-500"
              placeholder="Masukkan password"
              autoFocus
            />
          </div>
          <button
            type="submit"
            disabled={!adminPassword.trim() || authLoading}
            className="w-full py-3 rounded-xl bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-medium transition-colors"
          >
            Masuk Dashboard
          </button>
        </form>
      </div>
    );
  }

  return (
    <div className="h-screen bg-[#dfe6ef] font-sans text-slate-800 overflow-hidden">
      <div className="h-full flex flex-col bg-white border border-slate-300 shadow-xl">
        <div className="h-7 bg-[#f8fafc] border-b border-slate-200 flex items-center px-3 text-[11px]">
          <div className="flex gap-1.5 mr-4">
            <span className="w-3 h-3 rounded-full bg-red-400" />
            <span className="w-3 h-3 rounded-full bg-amber-400" />
            <span className="w-3 h-3 rounded-full bg-emerald-400" />
          </div>
          <div className="flex-1 text-center font-semibold text-slate-600">Labkom - Lab Komputer 1 - {activeClassName}</div>
          <div className="text-slate-500">Sesi: {currentTime.toLocaleTimeString('id-ID', { hour: '2-digit', minute: '2-digit' })} - Admin Lab</div>
        </div>

        <div className="h-7 bg-white border-b border-slate-200 flex items-center justify-between px-3 text-[11px]">
          <div className="flex items-center gap-4">
            {['File', 'Edit', 'View', 'Siswa', 'Kelas', 'Tools', 'Bantuan'].map((item) => (
              <button key={item} className={`px-2 py-1 rounded ${item === 'View' ? 'bg-blue-100 text-blue-700' : 'hover:bg-slate-100 text-slate-700'}`}>{item}</button>
            ))}
          </div>
          <button onClick={handleAdminLogout} className="flex items-center gap-1 text-slate-500 hover:text-red-600">
            <LogOut className="w-3.5 h-3.5" /> Keluar
          </button>
        </div>

        <div className="h-14 bg-white border-b border-slate-200 flex items-center justify-between px-3">
          <div className="flex items-center gap-1">
            {[
              { id: 'monitoring', label: 'Awasi', icon: Eye },
              { id: 'screens', label: 'Layar', icon: Monitor },
              { id: 'screenshare', label: 'Tayangkan', icon: BookOpen },
              { id: 'students', label: 'Siswa', icon: Users },
              { id: 'history', label: 'Riwayat', icon: History },
              { id: 'checks', label: 'Pengecekan', icon: ClipboardList },
              { id: 'activities', label: 'Aktivitas', icon: Activity },
              { id: 'control', label: 'Pengaturan', icon: Settings },
              { id: 'server', label: 'Server', icon: Server },
            ].map(({ id, label, icon: Icon }) => (
              <button
                key={id}
                onClick={() => setActiveTab(id)}
                className={`w-16 h-11 rounded flex flex-col items-center justify-center gap-0.5 text-[10px] ${activeTab === id ? 'bg-blue-50 text-blue-700' : 'text-slate-600 hover:bg-slate-50'}`}
              >
                <Icon className="w-4 h-4" />
                <span>{label}</span>
              </button>
            ))}
          </div>
          <div className="flex items-center gap-2">
            <ServerInfoBanner info={serverInfo} />
            <button onClick={handleCheckUpdate} className="h-8 px-3 rounded border border-slate-200 text-xs text-slate-600 hover:bg-slate-50 flex items-center gap-1.5">
              <DownloadCloud className="w-3.5 h-3.5" /> Update
            </button>
          </div>
        </div>

        <UpdateBanner status={updateStatus} onCheck={handleCheckUpdate} onDownload={handleDownloadUpdate} onInstall={handleInstallUpdate} />

        <div className="flex flex-1 min-h-0">
          {renderLabSidebar()}
          <main className="flex-1 min-w-0 min-h-0">
            {renderWorkspaceContent()}
          </main>
        </div>

        <div className="h-5 bg-blue-700 text-white text-[10px] flex items-center justify-between px-3">
          <span className="flex items-center gap-2"><span className="w-2 h-2 rounded-full bg-emerald-300" /> Server Aktif</span>
          <span>{onlineCount} online - {offlineCount} offline - {activePcs.length} aktif</span>
          <span>Kelas {activeClassName}</span>
          <span>Diag/Win - {currentTime.toLocaleTimeString('id-ID')}</span>
        </div>
      </div>

      {/* SIDEBAR */}
      <aside className="hidden">
        <div className="p-6 border-b border-slate-800 flex items-center space-x-3">
          <img src="./logo-sekolah.png" alt="Logo SPH" className="w-10 h-10 object-contain rounded-lg" onError={(e) => { e.target.style.display = 'none'; }} />
          <div><h1 className="text-xl font-bold tracking-wide">SPH LabKom</h1><p className="text-xs text-slate-400">Admin Dashboard</p></div>
        </div>

        <nav className="flex-1 p-4 space-y-2">
          {[
            { id: 'monitoring', label: 'Pemantauan PC',        icon: Monitor        },
            { id: 'screens',    label: 'Layar Siswa',          icon: Eye            },
            { id: 'control',    label: 'Kontrol & Kebijakan',  icon: Settings       },
            { id: 'students',   label: 'Data Siswa',           icon: Users          },
            { id: 'history',    label: 'Riwayat Sesi',         icon: History        },
            { id: 'checks',     label: 'Pengecekan Fasilitas', icon: ClipboardList  },
            { id: 'activities', label: 'Aktivitas Siswa',      icon: Activity       },
            { id: 'chat',       label: 'Pesan & Chat',         icon: MessageCircle  },
            { id: 'screenshare', label: 'Berbagi Layar',       icon: Monitor        },
            { id: 'server',     label: 'Status Server',        icon: Server         },
          ].map(({ id, label, icon: Icon }) => (
            <button
              key={id}
              onClick={() => setActiveTab(id)}
              className={`w-full flex items-center space-x-3 px-4 py-3 rounded-xl transition-all
                ${activeTab === id ? 'bg-blue-600 text-white shadow-lg shadow-blue-900/50' : 'text-slate-400 hover:bg-slate-800 hover:text-white'}`}
            >
              <Icon className="w-5 h-5" />
              <span className="font-medium">{label}</span>
            </button>
          ))}
        </nav>

        <div className="p-4 border-t border-slate-800">
          <div className="flex items-center space-x-3 mb-4 px-2">
            <div className="w-10 h-10 rounded-full bg-slate-700 flex items-center justify-center">
              <span className="font-bold text-sm">KL</span>
            </div>
            <div>
              <p className="text-sm font-semibold">Kepala Lab</p>
              <p className="text-xs text-green-400 flex items-center"><span className="w-2 h-2 rounded-full bg-green-400 mr-1" /> Online</p>
            </div>
          </div>
          <button onClick={handleAdminLogout} className="w-full flex items-center justify-center space-x-2 px-4 py-2.5 bg-slate-800 hover:bg-red-500/20 text-slate-300 hover:text-red-400 rounded-lg transition-colors text-sm font-medium">
            <LogOut className="w-4 h-4" /><span>Keluar Sistem</span>
          </button>
          <button
            onClick={handleCheckUpdate}
            className="mt-2 w-full flex items-center justify-center space-x-2 px-4 py-2 text-slate-500 hover:text-slate-300 hover:bg-slate-800 rounded-lg transition-colors text-xs"
          >
            <DownloadCloud className="w-3.5 h-3.5" />
            <span>Periksa Pembaruan</span>
          </button>
        </div>
      </aside>

      {/* MAIN CONTENT */}
      <main className="hidden">
        <header className="bg-white border-b border-slate-200 px-6 py-4 flex justify-between items-center sticky top-0 z-10 gap-4 flex-wrap">
          <div>
            <h2 className="text-2xl font-bold text-slate-800">
              {activeTab === 'monitoring' && 'Pemantauan Lab Real-time'}
              {activeTab === 'screens'    && 'Layar Siswa — Live View'}
              {activeTab === 'control'    && 'Kontrol & Kebijakan Lab'}
              {activeTab === 'students'  && 'Manajemen Data Siswa'}
              {activeTab === 'history'   && 'Riwayat Sesi Praktikum'}
              {activeTab === 'checks'    && 'Log Pengecekan Fasilitas'}
              {activeTab === 'activities' && 'Monitoring Aktivitas Siswa'}
              {activeTab === 'chat'        && 'Pesan & Komunikasi'}
              {activeTab === 'screenshare' && 'Berbagi Layar ke Siswa'}
              {activeTab === 'server'      && 'Status Server'}
            </h2>
            <p className="text-sm text-slate-500 mt-0.5">Mengelola akses dan data aktivitas lab komputer.</p>
          </div>
          <div className="flex items-center gap-4">
            {/* Banner IP server – terlihat jelas agar mudah dikonfigurasi di client PC */}
            <ServerInfoBanner info={serverInfo} />
            <div className="text-right hidden sm:block">
              <p className="text-lg font-mono font-bold text-blue-600">
                {currentTime.toLocaleTimeString('id-ID', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
              </p>
              <p className="text-xs text-slate-500 font-medium uppercase tracking-wider">
                {currentTime.toLocaleDateString('id-ID', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })}
              </p>
            </div>
          </div>
        </header>

        {/* Banner update (tampil otomatis ketika ada versi baru) */}
        <UpdateBanner
          status={updateStatus}
          onCheck={handleCheckUpdate}
          onDownload={handleDownloadUpdate}
          onInstall={handleInstallUpdate}
        />

        <div className="p-8 max-w-7xl mx-auto w-full">
          {activeTab === 'monitoring' && renderMonitoring()}
          {activeTab === 'screens'    && renderScreens()}
          {activeTab === 'control'    && renderControl()}
          {activeTab === 'students'  && renderStudents()}
          {activeTab === 'history'   && renderHistory()}
          {activeTab === 'checks'    && renderChecks()}
          {activeTab === 'activities'  && <ActivityMonitor serverUrl={API} socket={realtimeSocketRef.current} />}
          {activeTab === 'screenshare' && <ScreenShareAdmin socket={realtimeSocketRef.current} />}
          {activeTab === 'server'      && renderServer()}
        </div>
      </main>

      {/* ─── MODAL: Kontrol Individual PC ────────────────────────────────── */}
      {controlModalPc && (
        <div className="fixed inset-0 bg-slate-900/40 backdrop-blur-sm z-50 flex items-center justify-center p-4 animate-in fade-in">
          <div className="bg-white rounded-2xl shadow-2xl max-w-md w-full overflow-hidden animate-in zoom-in-95 duration-500">
            <div className="bg-slate-900 p-5 flex justify-between items-center text-white">
              <div className="flex items-center space-x-3">
                <Monitor className="w-5 h-5 text-blue-400" />
                <h3 className="font-bold text-lg">Kontrol Klien: {controlModalPc.id}</h3>
              </div>
              <button onClick={() => setControlModalPc(null)} className="text-slate-400 hover:text-white transition-colors">
                <X className="w-5 h-5" />
              </button>
            </div>
            <div className="p-6 space-y-6">
              {/* Info Pengguna */}
              {controlModalPc.status === 'active' ? (
                <div className="bg-blue-50 p-4 rounded-xl border border-blue-100 flex items-start justify-between">
                  <div>
                    <p className="text-xs text-blue-500 font-bold uppercase tracking-wider mb-1">Sedang Digunakan</p>
                    <p className="font-semibold text-slate-800">{controlModalPc.student.name}</p>
                    <p className="text-sm text-slate-600">NIS: {controlModalPc.student.nis} · {controlModalPc.student.kelas}</p>
                  </div>
                  <button
                    onClick={() => handleForceLogout(controlModalPc)}
                    className="p-2 bg-red-100 text-red-600 hover:bg-red-200 rounded-lg transition-colors"
                    title="Paksa Keluar"
                  ><LogOut className="w-5 h-5" /></button>
                </div>
              ) : (
                <div className="bg-slate-50 p-4 rounded-xl border border-slate-200">
                  <p className="text-sm font-medium text-slate-600 text-center">
                    {controlModalPc.status === 'locked' ? 'PC Terkunci (Siap Digunakan)' : 'PC Sedang Offline'}
                  </p>
                </div>
              )}

              {/* Daya per PC */}
              <div>
                <p className="text-xs font-bold text-slate-400 uppercase tracking-wider mb-3">Manajemen Daya</p>
                <p className="text-xs text-amber-600 mb-3">Membutuhkan agent di PC client.</p>
                <div className="flex space-x-3">
                  <button disabled={controlModalPc.status === 'offline'} className="flex-1 py-2.5 bg-white border border-slate-300 text-slate-700 hover:bg-red-50 hover:text-red-600 hover:border-red-200 disabled:opacity-50 disabled:cursor-not-allowed rounded-xl text-sm font-medium transition-colors flex items-center justify-center space-x-2">
                    <Power className="w-4 h-4" /><span>Shutdown</span>
                  </button>
                  <button disabled={controlModalPc.status === 'offline'} className="flex-1 py-2.5 bg-white border border-slate-300 text-slate-700 hover:bg-indigo-50 hover:text-indigo-600 hover:border-indigo-200 disabled:opacity-50 disabled:cursor-not-allowed rounded-xl text-sm font-medium transition-colors flex items-center justify-center space-x-2">
                    <Moon className="w-4 h-4" /><span>Sleep</span>
                  </button>
                </div>
              </div>

              {/* Volume per PC */}
              <div>
                <p className="text-xs font-bold text-slate-400 uppercase tracking-wider mb-3">Volume PC Ini</p>
                <div className="flex items-center space-x-3">
                  <Volume2 className="w-5 h-5 text-slate-400" />
                  <input type="range" min="0" max="100" defaultValue="75" disabled={controlModalPc.status === 'offline'} className="flex-1 h-2 bg-slate-200 rounded-lg appearance-none cursor-pointer accent-blue-600 disabled:opacity-50" />
                </div>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* ─── MODAL: Konfirmasi Force Logout (satu PC) ─────────────────────── */}
      {showLogoutModal && selectedPc && (
        <div className="fixed inset-0 bg-slate-900/50 backdrop-blur-sm z-50 flex items-center justify-center p-4 animate-in fade-in">
          <div className="bg-white rounded-2xl shadow-xl max-w-md w-full p-6 animate-in zoom-in-95 duration-500">
            <div className="flex items-center justify-center w-16 h-16 rounded-full bg-red-100 text-red-600 mx-auto mb-4">
              <AlertTriangle className="w-8 h-8" />
            </div>
            <h3 className="text-xl font-bold text-center text-slate-800 mb-2">Paksa Keluar Siswa?</h3>
            <p className="text-center text-slate-500 mb-6">
              Anda yakin ingin memaksa keluar <strong className="text-slate-700">{selectedPc.student?.name}</strong> dari <strong className="text-slate-700">{selectedPc.id}</strong>?
            </p>
            <div className="flex space-x-3">
              <button onClick={() => setShowLogoutModal(false)} className="flex-1 py-2.5 bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 rounded-xl font-medium transition-colors">Batal</button>
              <button onClick={confirmForceLogout} className="flex-1 py-2.5 bg-red-600 hover:bg-red-700 text-white rounded-xl font-medium transition-colors shadow-lg shadow-red-600/20">Ya, Paksa Keluar</button>
            </div>
          </div>
        </div>
      )}

      {/* ─── MODAL: Konfirmasi Force Logout Semua ─────────────────────────── */}
      {confirmLogoutAll && (
        <div className="fixed inset-0 bg-slate-900/50 backdrop-blur-sm z-50 flex items-center justify-center p-4 animate-in fade-in">
          <div className="bg-white rounded-2xl shadow-xl max-w-md w-full p-6 animate-in zoom-in-95 duration-500">
            <div className="flex items-center justify-center w-16 h-16 rounded-full bg-amber-100 text-amber-600 mx-auto mb-4">
              <AlertTriangle className="w-8 h-8" />
            </div>
            <h3 className="text-xl font-bold text-center text-slate-800 mb-2">Kunci Semua PC?</h3>
            <p className="text-center text-slate-500 mb-6">
              Seluruh siswa yang sedang aktif akan dipaksa keluar. Tindakan ini tidak dapat dibatalkan.
            </p>
            <div className="flex space-x-3">
              <button onClick={() => setConfirmLogoutAll(false)} className="flex-1 py-2.5 bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 rounded-xl font-medium transition-colors">Batal</button>
              <button onClick={handleForceLogoutAll} className="flex-1 py-2.5 bg-amber-600 hover:bg-amber-700 text-white rounded-xl font-medium transition-colors shadow-lg shadow-amber-600/20">Ya, Kunci Semua</button>
            </div>
          </div>
        </div>
      )}

      {/* ─── MODAL: Nonaktifkan Siswa ─────────────────────────────────────── */}
      {deleteTarget && (
        <div className="fixed inset-0 bg-slate-900/50 backdrop-blur-sm z-50 flex items-center justify-center p-4 animate-in fade-in">
          <div className="bg-white rounded-2xl shadow-xl max-w-md w-full p-6 animate-in zoom-in-95 duration-500">
            <div className="flex items-center justify-center w-16 h-16 rounded-full bg-red-100 text-red-600 mx-auto mb-4">
              <Trash2 className="w-8 h-8" />
            </div>
            <h3 className="text-xl font-bold text-center text-slate-800 mb-2">Nonaktifkan Siswa?</h3>
            <p className="text-center text-slate-500 mb-6">
              Akun <strong className="text-slate-700">{deleteTarget.nama_lengkap}</strong> (NIS: {deleteTarget.nis}) akan dinonaktifkan. Data riwayat tetap disimpan.
            </p>
            <div className="flex space-x-3">
              <button onClick={() => setDeleteTarget(null)} className="flex-1 py-2.5 bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 rounded-xl font-medium transition-colors">Batal</button>
              <button onClick={confirmDeleteStudent} className="flex-1 py-2.5 bg-red-600 hover:bg-red-700 text-white rounded-xl font-medium transition-colors">Ya, Nonaktifkan</button>
            </div>
          </div>
        </div>
      )}

      {/* ─── MODAL: Tambah / Edit Siswa ───────────────────────────────────── */}
      {stuModal && (
        <StudentModal
          student={stuModal === 'add' ? null : stuModal}
          onClose={() => setStuModal(null)}
          onSaved={() => { setStuModal(null); fetchStudents(); showToast('Data siswa berhasil disimpan.'); }}
        />
      )}

      {/* ─── Toast Notifikasi ─────────────────────────────────────────────── */}
      {toast && <Toast message={toast.message} type={toast.type} onClose={() => setToast(null)} />}

      {/* ─── Chat Panel (selalu mounted agar pesan tidak hilang saat ganti tab) ── */}
      <ChatPanel socket={realtimeSocketRef.current} />

      {/* ─── Attention Mode (kunci layar semua siswa) ── */}
      <AttentionModeButton socket={realtimeSocketRef.current} />
    </div>
  );
}

