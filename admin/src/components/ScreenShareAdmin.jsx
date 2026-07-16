import React, { useEffect, useMemo, useRef, useState } from 'react';
import { AlertCircle, CheckSquare, Gauge, Monitor, MonitorOff, Pause, Play, Square, Users, Wifi } from 'lucide-react';

const PROFILES = {
  performance: { label: 'Hemat Jaringan', fps: 6, width: 1280, height: 720, jpegQuality: 0.52 },
  balanced: { label: 'Seimbang', fps: 10, width: 1600, height: 900, jpegQuality: 0.64 },
  quality: { label: 'Kualitas Tinggi', fps: 12, width: 1920, height: 1080, jpegQuality: 0.72 },
};

function emitWithAck(socket, eventName, payload, timeoutMs = 4000) {
  return new Promise((resolve) => {
    let settled = false;
    const finish = (result) => {
      if (settled) return;
      settled = true;
      resolve(result);
    };
    const timer = setTimeout(() => finish({ success: false, message: 'Server tidak merespons.' }), timeoutMs);
    socket.emit(eventName, payload, (result) => {
      clearTimeout(timer);
      finish(result || { success: false });
    });
  });
}

function canvasToBlob(canvas, quality) {
  return new Promise((resolve) => canvas.toBlob(resolve, 'image/jpeg', quality));
}

/**
 * Broadcast layar instruktur v2: frame biner, target all/selected, satu frame
 * in-flight, dan kualitas JPEG adaptif berdasarkan acknowledgement server.
 */
export default function ScreenShareAdmin({ socket }) {
  const [sharing, setSharing] = useState(false);
  const [paused, setPaused] = useState(false);
  const [error, setError] = useState('');
  const [onlineClients, setOnlineClients] = useState([]);
  const [targetMode, setTargetMode] = useState('all');
  const [selectedPcs, setSelectedPcs] = useState([]);
  const [profileName, setProfileName] = useState('balanced');
  const [stats, setStats] = useState({ fps: 0, latency: 0, delivered: 0, quality: 0.64, dropped: 0 });

  const streamRef = useRef(null);
  const videoRef = useRef(null);
  const canvasRef = useRef(null);
  const intervalRef = useRef(null);
  const ackTimeoutRef = useRef(null);
  const previewRef = useRef(null);
  const sharingRef = useRef(false);
  const pausedRef = useRef(false);
  const frameInFlightRef = useRef(false);
  const inFlightSequenceRef = useRef(0);
  const sequenceRef = useRef(0);
  const adaptiveQualityRef = useRef(PROFILES.balanced.jpegQuality);
  const sampleRef = useRef({ sent: 0, dropped: 0, sampledAt: Date.now() });

  const profile = PROFILES[profileName];
  const selectedSet = useMemo(() => new Set(selectedPcs), [selectedPcs]);
  const targetCount = targetMode === 'all' ? onlineClients.length : selectedPcs.length;

  useEffect(() => {
    if (!socket) return undefined;

    const applySnapshot = (entries = []) => {
      const online = Array.isArray(entries)
        ? entries.filter((entry) => entry?.pc_name && entry.is_online !== false)
        : [];
      online.sort((a, b) => a.pc_name.localeCompare(b.pc_name));
      setOnlineClients(online);
      setSelectedPcs((previous) => previous.filter((pcName) => online.some((entry) => entry.pc_name === pcName)));
    };

    const onPresence = (entry = {}) => {
      if (!entry.pc_name) return;
      setOnlineClients((previous) => {
        const next = previous.filter((item) => item.pc_name !== entry.pc_name);
        if (entry.is_online !== false) next.push(entry);
        next.sort((a, b) => a.pc_name.localeCompare(b.pc_name));
        return next;
      });
      if (entry.is_online === false) {
        setSelectedPcs((previous) => previous.filter((pcName) => pcName !== entry.pc_name));
      }
    };

    socket.on('presence:snapshot', applySnapshot);
    socket.on('presence:update', onPresence);
    socket.emit('admin:presence-snapshot-request');
    return () => {
      socket.off('presence:snapshot', applySnapshot);
      socket.off('presence:update', onPresence);
    };
  }, [socket]);

  const clearCaptureResources = () => {
    sharingRef.current = false;
    pausedRef.current = false;
    frameInFlightRef.current = false;
    inFlightSequenceRef.current = 0;
    clearInterval(intervalRef.current);
    clearTimeout(ackTimeoutRef.current);
    intervalRef.current = null;
    ackTimeoutRef.current = null;

    if (streamRef.current) {
      streamRef.current.getTracks().forEach((track) => track.stop());
      streamRef.current = null;
    }
    if (videoRef.current) {
      videoRef.current.srcObject = null;
      videoRef.current = null;
    }
    if (previewRef.current) previewRef.current.srcObject = null;
  };

  useEffect(() => {
    const onDisconnect = () => {
      if (!sharingRef.current) return;
      clearCaptureResources();
      setPaused(false);
      setSharing(false);
      setError('Koneksi realtime terputus. Mulai ulang siaran setelah server tersambung.');
    };
    socket?.on('disconnect', onDisconnect);

    return () => {
      socket?.off('disconnect', onDisconnect);
      if (sharingRef.current) socket?.emit('admin:screen-share-stop');
      clearCaptureResources();
    };
  }, [socket]);

  const sendFrame = async () => {
    if (!sharingRef.current || pausedRef.current || frameInFlightRef.current || !socket?.connected) return;
    const video = videoRef.current;
    const canvas = canvasRef.current;
    if (!video?.videoWidth || !canvas) return;
    frameInFlightRef.current = true;

    const scale = Math.min(1, profile.width / video.videoWidth, profile.height / video.videoHeight);
    canvas.width = Math.max(1, Math.round(video.videoWidth * scale));
    canvas.height = Math.max(1, Math.round(video.videoHeight * scale));
    const context = canvas.getContext('2d', { alpha: false });
    context.drawImage(video, 0, 0, canvas.width, canvas.height);

    const blob = await canvasToBlob(canvas, adaptiveQualityRef.current);
    if (!blob || !sharingRef.current || pausedRef.current) {
      frameInFlightRef.current = false;
      return;
    }
    let frame;
    try {
      frame = await blob.arrayBuffer();
    } catch {
      frameInFlightRef.current = false;
      sampleRef.current.dropped += 1;
      return;
    }
    if (!sharingRef.current || pausedRef.current) {
      frameInFlightRef.current = false;
      return;
    }

    const sentAt = Date.now();
    const sequence = ++sequenceRef.current;
    sampleRef.current.sent += 1;

    inFlightSequenceRef.current = sequence;
    const ackTimer = setTimeout(() => {
      if (inFlightSequenceRef.current !== sequence) return;
      inFlightSequenceRef.current = 0;
      frameInFlightRef.current = false;
      sampleRef.current.dropped += 1;
      adaptiveQualityRef.current = Math.max(0.42, adaptiveQualityRef.current - 0.04);
    }, 1500);
    ackTimeoutRef.current = ackTimer;

    socket.emit('admin:screen-share-frame-v2', {
      frame,
      mime: 'image/jpeg',
      width: canvas.width,
      height: canvas.height,
      sequence,
      sent_at: sentAt,
    }, (result = {}) => {
      clearTimeout(ackTimer);
      if (inFlightSequenceRef.current !== sequence) return;
      inFlightSequenceRef.current = 0;
      ackTimeoutRef.current = null;
      frameInFlightRef.current = false;
      if (result.success === false) {
        clearCaptureResources();
        setPaused(false);
        setSharing(false);
        setError(result.message || 'Sesi berbagi layar dihentikan server.');
        return;
      }

      const latency = Date.now() - sentAt;
      if (latency > 350) adaptiveQualityRef.current = Math.max(0.42, adaptiveQualityRef.current - 0.04);
      else if (latency < 140) adaptiveQualityRef.current = Math.min(profile.jpegQuality, adaptiveQualityRef.current + 0.015);

      const now = Date.now();
      const sample = sampleRef.current;
      const elapsed = now - sample.sampledAt;
      if (elapsed >= 1000) {
        setStats({
          fps: Math.round((sample.sent * 1000) / elapsed),
          latency,
          delivered: Number(result.count) || 0,
          quality: adaptiveQualityRef.current,
          dropped: sample.dropped,
        });
        sampleRef.current = { sent: 0, dropped: 0, sampledAt: now };
      }
    });
  };

  const startSharing = async () => {
    setError('');
    if (!socket?.connected) return setError('Koneksi realtime ke server belum aktif.');
    if (targetCount === 0) return setError('Tidak ada PC siswa yang dipilih atau sedang online.');

    try {
      const stream = await navigator.mediaDevices.getDisplayMedia({
        video: {
          frameRate: { ideal: profile.fps, max: 15 },
          width: { ideal: profile.width },
          height: { ideal: profile.height },
        },
        audio: false,
      });
      streamRef.current = stream;

      const video = document.createElement('video');
      video.muted = true;
      video.playsInline = true;
      video.srcObject = stream;
      await video.play();
      videoRef.current = video;
      if (previewRef.current) previewRef.current.srcObject = stream;

      const track = stream.getVideoTracks()[0];
      const targets = targetMode === 'all' ? 'all' : selectedPcs;
      const started = await emitWithAck(socket, 'admin:screen-share-start', {
        targets,
        profile: profileName,
        source_label: track?.label || 'Layar Instruktur',
      });
      if (!started?.success) throw new Error(started?.message || 'Server menolak sesi berbagi layar.');

      sharingRef.current = true;
      pausedRef.current = false;
      setPaused(false);
      sequenceRef.current = 0;
      adaptiveQualityRef.current = profile.jpegQuality;
      sampleRef.current = { sent: 0, dropped: 0, sampledAt: Date.now() };
      setStats({ fps: 0, latency: 0, delivered: started.count || 0, quality: profile.jpegQuality, dropped: 0 });
      setSharing(true);

      track?.addEventListener('ended', () => stopSharing(), { once: true });
      intervalRef.current = setInterval(sendFrame, Math.round(1000 / profile.fps));
      sendFrame();
    } catch (captureError) {
      clearCaptureResources();
      setSharing(false);
      if (captureError?.name !== 'NotAllowedError') {
        setError(`Gagal memulai berbagi layar: ${captureError?.message || 'unknown error'}`);
      }
    }
  };

  const stopSharing = () => {
    const wasSharing = sharingRef.current;
    clearCaptureResources();
    if (wasSharing) socket?.emit('admin:screen-share-stop');
    setPaused(false);
    setSharing(false);
  };

  const togglePause = async () => {
    const nextPaused = !pausedRef.current;
    const result = await emitWithAck(socket, 'admin:screen-share-pause', { paused: nextPaused });
    if (!result?.success) {
      setError(result?.message || 'Gagal mengubah status jeda.');
      return;
    }
    pausedRef.current = nextPaused;
    setPaused(nextPaused);
  };

  const togglePc = (pcName) => {
    if (sharing) return;
    setSelectedPcs((previous) => previous.includes(pcName)
      ? previous.filter((item) => item !== pcName)
      : [...previous, pcName]);
  };

  return (
    <div className="space-y-5">
      <div className="bg-white rounded-2xl border border-slate-200 shadow-sm p-6">
        <div className="flex flex-wrap items-start justify-between gap-4 mb-5">
          <div className="flex items-center gap-3">
            <div className={`p-2.5 rounded-xl ${sharing ? 'bg-red-100 text-red-600' : 'bg-blue-100 text-blue-600'}`}>
              <Monitor className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-lg font-bold text-slate-800">Siaran Layar Instruktur</h3>
              <p className="text-sm text-slate-500">Transport biner adaptif untuk seluruh kelas atau PC terpilih</p>
            </div>
          </div>
          <div className="flex items-center gap-2 text-sm text-slate-500">
            <Wifi className={`w-4 h-4 ${socket?.connected ? 'text-emerald-500' : 'text-red-400'}`} />
            <Users className="w-4 h-4" />
            <span>{onlineClients.length} PC online</span>
          </div>
        </div>

        {error && (
          <div className="mb-5 flex items-center gap-2 text-sm text-red-700 bg-red-50 border border-red-200 rounded-xl px-4 py-3">
            <AlertCircle className="w-4 h-4 flex-shrink-0" />
            <span>{error}</span>
          </div>
        )}

        <div className="grid lg:grid-cols-2 gap-5 mb-5">
          <div className="rounded-xl border border-slate-200 p-4">
            <p className="text-xs font-bold uppercase tracking-wider text-slate-500 mb-3">Target siaran</p>
            <div className="flex gap-2 mb-3">
              <button type="button" disabled={sharing} onClick={() => setTargetMode('all')}
                className={`px-3 py-2 rounded-lg text-sm font-medium border ${targetMode === 'all' ? 'bg-blue-600 border-blue-600 text-white' : 'border-slate-200 text-slate-600'}`}>
                Semua PC ({onlineClients.length})
              </button>
              <button type="button" disabled={sharing} onClick={() => setTargetMode('selected')}
                className={`px-3 py-2 rounded-lg text-sm font-medium border ${targetMode === 'selected' ? 'bg-blue-600 border-blue-600 text-white' : 'border-slate-200 text-slate-600'}`}>
                PC terpilih ({selectedPcs.length})
              </button>
            </div>
            {targetMode === 'selected' && (
              <div className="max-h-36 overflow-auto grid sm:grid-cols-2 gap-1.5 pr-1">
                {onlineClients.map((client) => (
                  <button key={client.pc_name} type="button" disabled={sharing} onClick={() => togglePc(client.pc_name)}
                    className={`flex items-center gap-2 px-2.5 py-2 rounded-lg text-left text-xs ${selectedSet.has(client.pc_name) ? 'bg-blue-50 text-blue-700' : 'bg-slate-50 text-slate-600'}`}>
                    {selectedSet.has(client.pc_name) ? <CheckSquare className="w-4 h-4" /> : <Square className="w-4 h-4" />}
                    <span className="font-semibold truncate">{client.pc_name}</span>
                    {client.student_name && <span className="truncate text-slate-400">{client.student_name}</span>}
                  </button>
                ))}
              </div>
            )}
          </div>

          <div className="rounded-xl border border-slate-200 p-4">
            <p className="text-xs font-bold uppercase tracking-wider text-slate-500 mb-3">Profil kualitas</p>
            <div className="space-y-2">
              {Object.entries(PROFILES).map(([name, item]) => (
                <button key={name} type="button" disabled={sharing} onClick={() => setProfileName(name)}
                  className={`w-full flex items-center justify-between px-3 py-2 rounded-lg border text-sm ${profileName === name ? 'border-blue-500 bg-blue-50 text-blue-700' : 'border-slate-200 text-slate-600'}`}>
                  <span className="font-medium">{item.label}</span>
                  <span className="text-xs font-mono">{item.width}x{item.height} · {item.fps} FPS</span>
                </button>
              ))}
            </div>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-4">
          {!sharing ? (
            <button onClick={startSharing} disabled={!socket?.connected || targetCount === 0}
              className="flex items-center gap-2 px-6 py-3 bg-blue-600 hover:bg-blue-700 disabled:bg-slate-300 text-white rounded-xl font-semibold transition-colors">
              <Monitor className="w-5 h-5" />
              Mulai Siaran
            </button>
          ) : (
            <button onClick={stopSharing}
              className="flex items-center gap-2 px-6 py-3 bg-red-600 hover:bg-red-700 text-white rounded-xl font-semibold transition-colors">
              <MonitorOff className="w-5 h-5" />
              Hentikan Siaran
            </button>
          )}

          {sharing && (
            <button onClick={togglePause}
              className="flex items-center gap-2 px-4 py-3 bg-slate-100 hover:bg-slate-200 text-slate-700 rounded-xl font-semibold transition-colors">
              {paused ? <Play className="w-4 h-4" /> : <Pause className="w-4 h-4" />}
              {paused ? 'Lanjutkan' : 'Jeda'}
            </button>
          )}

          {sharing && (
            <div className="flex flex-wrap items-center gap-3 text-xs text-slate-600">
              <span className="flex items-center gap-1.5 text-emerald-600 font-semibold"><span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />{paused ? 'DIJEDA' : 'LIVE'}</span>
              <span className="flex items-center gap-1"><Gauge className="w-4 h-4" />{stats.fps} FPS · {stats.latency} ms</span>
              <span>{stats.delivered} penerima</span>
              <span>Q {Math.round(stats.quality * 100)}%</span>
              {stats.dropped > 0 && <span className="text-amber-600">{stats.dropped} frame dilewati</span>}
            </div>
          )}
        </div>

        {sharing && (
          <div className="mt-6">
            <p className="text-xs font-bold uppercase tracking-wider text-slate-500 mb-2">Pratinjau lokal</p>
            <div className="relative bg-slate-950 rounded-xl overflow-hidden aspect-video max-w-3xl">
              <video ref={previewRef} autoPlay muted className="w-full h-full object-contain" />
              <span className="absolute top-3 right-3 bg-red-600 text-white text-[11px] font-bold px-2.5 py-1 rounded-full">LIVE</span>
            </div>
          </div>
        )}

        <canvas ref={canvasRef} className="hidden" />
      </div>
    </div>
  );
}
