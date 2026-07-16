import React, { useEffect, useRef, useState } from 'react';
import { Monitor } from 'lucide-react';

function frameToBlob(frame, mime = 'image/jpeg') {
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

/**
 * Viewer broadcast instruktur. Transport v2 memakai frame biner/Object URL agar
 * tidak menggandakan ukuran data seperti base64; event lama tetap didukung.
 */
export default function AdminScreenShare({ socket }) {
  const [active, setActive] = useState(false);
  const [paused, setPaused] = useState(false);
  const [frameUrl, setFrameUrl] = useState(null);
  const [metadata, setMetadata] = useState({ source_label: 'Layar Instruktur' });
  const [stats, setStats] = useState({ fps: 0, latency: 0, width: 0, height: 0, transport: 'binary' });
  const currentUrlRef = useRef(null);
  const objectUrlsRef = useRef(new Set());
  const frameStatsRef = useRef({ count: 0, sampledAt: Date.now() });

  const releaseObjectUrls = (except = null) => {
    for (const url of objectUrlsRef.current) {
      if (url === except) continue;
      URL.revokeObjectURL(url);
      objectUrlsRef.current.delete(url);
    }
  };

  useEffect(() => {
    if (!socket) return undefined;

    const onStart = (data = {}) => {
      setActive(true);
      setPaused(Boolean(data.paused));
      setMetadata({ source_label: data.source_label || 'Layar Instruktur', ...data });
      setFrameUrl(null);
      currentUrlRef.current = null;
      releaseObjectUrls();
      frameStatsRef.current = { count: 0, sampledAt: Date.now() };
    };

    const onLegacyFrame = (data = {}) => {
      if (!data.image) return;
      releaseObjectUrls();
      currentUrlRef.current = null;
      setFrameUrl(data.image);
      setStats((previous) => ({ ...previous, transport: 'compatibility' }));
    };

    const onBinaryFrame = (data = {}) => {
      const blob = frameToBlob(data.frame, data.mime);
      if (!blob) return;

      const nextUrl = URL.createObjectURL(blob);
      objectUrlsRef.current.add(nextUrl);
      currentUrlRef.current = nextUrl;
      setFrameUrl(nextUrl);

      const now = Date.now();
      const sample = frameStatsRef.current;
      sample.count += 1;
      const elapsed = now - sample.sampledAt;
      if (elapsed >= 1000) {
        setStats({
          fps: Math.round((sample.count * 1000) / elapsed),
          latency: Math.max(0, now - (Number(data.sent_at) || now)),
          width: Number(data.width) || 0,
          height: Number(data.height) || 0,
          transport: 'binary',
        });
        frameStatsRef.current = { count: 0, sampledAt: now };
      }
    };

    const onPause = ({ paused: nextPaused } = {}) => {
      setPaused(Boolean(nextPaused));
    };

    const onStop = () => {
      setActive(false);
      setPaused(false);
      setFrameUrl(null);
      currentUrlRef.current = null;
      releaseObjectUrls();
    };

    socket.on('admin:screen-share-start', onStart);
    socket.on('admin:screen-share-frame', onLegacyFrame);
    socket.on('admin:screen-share-frame-v2', onBinaryFrame);
    socket.on('admin:screen-share-pause', onPause);
    socket.on('admin:screen-share-stop', onStop);

    return () => {
      socket.off('admin:screen-share-start', onStart);
      socket.off('admin:screen-share-frame', onLegacyFrame);
      socket.off('admin:screen-share-frame-v2', onBinaryFrame);
      socket.off('admin:screen-share-pause', onPause);
      socket.off('admin:screen-share-stop', onStop);
      releaseObjectUrls();
    };
  }, [socket]);

  if (!active) return null;

  return (
    <div
      className="fixed inset-0 z-[9999] bg-black flex flex-col"
      style={{ userSelect: 'none', pointerEvents: 'all' }}
    >
      <div className="flex items-center gap-3 px-5 py-3 bg-slate-900 border-b border-slate-700 flex-shrink-0">
        <Monitor className="w-5 h-5 text-blue-400" />
        <div className="min-w-0">
          <p className="font-semibold text-sm text-white truncate">{metadata.source_label}</p>
          <p className="text-[11px] text-slate-400">Disiarkan oleh instruktur</p>
        </div>
        <div className={`flex items-center gap-1.5 ml-2 text-xs font-semibold uppercase tracking-wider ${paused ? 'text-amber-400' : 'text-red-400'}`}>
          <span className={`w-2 h-2 rounded-full ${paused ? 'bg-amber-400' : 'bg-red-500 animate-pulse'}`} />
          {paused ? 'DIJEDA' : 'LIVE'}
        </div>
        <div className="ml-auto hidden sm:flex items-center gap-3 text-[11px] text-slate-400 font-mono">
          {stats.width > 0 && <span>{stats.width}x{stats.height}</span>}
          {stats.fps > 0 && <span>{stats.fps} FPS</span>}
          {stats.transport === 'binary' && stats.latency > 0 && <span>{stats.latency} ms</span>}
        </div>
      </div>

      <div className="relative flex-1 flex items-center justify-center bg-black overflow-hidden">
        {frameUrl ? (
          <img
            src={frameUrl}
            alt="Layar instruktur"
            className="w-full h-full object-contain"
            draggable={false}
            onLoad={() => releaseObjectUrls(currentUrlRef.current)}
          />
        ) : (
          <div className="flex flex-col items-center space-y-4 text-slate-500">
            <Monitor className="w-16 h-16 opacity-30" />
            <p className="text-sm">Menyiapkan siaran layar...</p>
          </div>
        )}
        {paused && (
          <div className="absolute inset-0 flex items-center justify-center bg-black/35">
            <span className="px-4 py-2 rounded-full bg-slate-900/90 text-white text-sm font-semibold">Siaran dijeda</span>
          </div>
        )}
      </div>
    </div>
  );
}
