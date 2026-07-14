import React, { useState, useEffect, useRef } from 'react';
import { Monitor } from 'lucide-react';

/**
 * AdminScreenShare - Tampil di client ketika admin sedang berbagi layar.
 * Overlay fullscreen yang tidak bisa ditutup oleh siswa.
 */
export default function AdminScreenShare({ socket }) {
  const [active, setActive] = useState(false);
  const [frame, setFrame]   = useState(null);
  const imgRef = useRef(null);

  useEffect(() => {
    if (!socket) return;

    const onStart = () => {
      setActive(true);
      setFrame(null);
    };

    const onFrame = (data) => {
      if (data?.image) setFrame(data.image);
    };

    const onStop = () => {
      setActive(false);
      setFrame(null);
    };

    socket.on('admin:screen-share-start', onStart);
    socket.on('admin:screen-share-frame', onFrame);
    socket.on('admin:screen-share-stop',  onStop);

    return () => {
      socket.off('admin:screen-share-start', onStart);
      socket.off('admin:screen-share-frame', onFrame);
      socket.off('admin:screen-share-stop',  onStop);
    };
  }, [socket]);

  if (!active) return null;

  return (
    <div
      className="fixed inset-0 z-[9999] bg-black flex flex-col"
      style={{ userSelect: 'none', pointerEvents: 'all' }}
    >
      {/* Header bar */}
      <div className="flex items-center space-x-3 px-5 py-3 bg-slate-900 border-b border-slate-700 flex-shrink-0">
        <div className="flex items-center space-x-2 text-white">
          <Monitor className="w-5 h-5 text-blue-400" />
          <span className="font-semibold text-sm">Layar Instruktur</span>
        </div>
        <div className="flex items-center space-x-1.5 ml-2">
          <span className="w-2 h-2 rounded-full bg-red-500 animate-pulse" />
          <span className="text-xs text-red-400 font-semibold uppercase tracking-wider">LIVE</span>
        </div>
        <p className="ml-auto text-xs text-slate-400">Perhatikan layar instruktur</p>
      </div>

      {/* Frame area */}
      <div className="flex-1 flex items-center justify-center bg-black overflow-hidden">
        {frame ? (
          <img
            ref={imgRef}
            src={frame}
            alt="Layar Admin"
            className="max-w-full max-h-full object-contain"
            draggable={false}
          />
        ) : (
          <div className="flex flex-col items-center space-y-4 text-slate-500">
            <Monitor className="w-16 h-16 opacity-30" />
            <p className="text-sm">Menunggu layar instruktur...</p>
          </div>
        )}
      </div>
    </div>
  );
}
