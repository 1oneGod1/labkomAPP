import React, { useState, useRef, useEffect } from 'react';
import { Monitor, MonitorOff, Users, Loader2, AlertCircle } from 'lucide-react';

/**
 * ScreenShareAdmin - Admin dapat berbagi layar ke semua client yang sedang login
 * Menggunakan getDisplayMedia() untuk capture layar admin, lalu kirim frame
 * sebagai base64 image via socket ke semua client.
 */
export default function ScreenShareAdmin({ socket }) {
  const [sharing, setSharing]       = useState(false);
  const [error, setError]           = useState('');
  const [clientCount, setClientCount] = useState(0);
  const streamRef    = useRef(null);
  const canvasRef    = useRef(null);
  const intervalRef  = useRef(null);
  const previewRef   = useRef(null);

  // Track online clients
  useEffect(() => {
    if (!socket) return;
    const onlineClients = new Map();
    const handler = (data) => {
      if (!data?.pc_name) return;
      if (data.is_online) onlineClients.set(data.pc_name, true);
      else onlineClients.delete(data.pc_name);
      setClientCount(onlineClients.size);
    };
    socket.on('presence:update', handler);
    return () => socket.off('presence:update', handler);
  }, [socket]);

  // Stop sharing when component unmounts
  useEffect(() => {
    return () => stopSharing();
  }, []);

  const startSharing = async () => {
    setError('');
    try {
      const stream = await navigator.mediaDevices.getDisplayMedia({
        video: { frameRate: 5, width: { ideal: 1280 }, height: { ideal: 720 } },
        audio: false,
      });

      streamRef.current = stream;

      // Show preview
      if (previewRef.current) {
        previewRef.current.srcObject = stream;
      }

      // When user stops sharing via browser UI
      stream.getVideoTracks()[0].addEventListener('ended', () => {
        stopSharing();
      });

      // Canvas for frame capture
      const canvas = canvasRef.current;
      const video  = document.createElement('video');
      video.srcObject = stream;
      video.play();

      setSharing(true);

      // Notify clients that admin is sharing
      socket?.emit('admin:screen-share-start');

      // Send frames every 200ms (5fps)
      intervalRef.current = setInterval(() => {
        if (!canvas || !video.videoWidth) return;
        canvas.width  = video.videoWidth;
        canvas.height = video.videoHeight;
        const ctx = canvas.getContext('2d');
        ctx.drawImage(video, 0, 0);
        const frame = canvas.toDataURL('image/jpeg', 0.6);
        socket?.emit('admin:screen-share-frame', { image: frame });
      }, 200);

    } catch (err) {
      if (err.name !== 'NotAllowedError') {
        setError('Gagal memulai berbagi layar: ' + err.message);
      }
    }
  };

  const stopSharing = () => {
    clearInterval(intervalRef.current);
    intervalRef.current = null;

    if (streamRef.current) {
      streamRef.current.getTracks().forEach(t => t.stop());
      streamRef.current = null;
    }

    if (previewRef.current) {
      previewRef.current.srcObject = null;
    }

    socket?.emit('admin:screen-share-stop');
    setSharing(false);
  };

  return (
    <div className="space-y-6">
      <div className="bg-white rounded-2xl border border-slate-200 shadow-sm p-6">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center space-x-3">
            <div className={`p-2 rounded-xl ${sharing ? 'bg-red-100 text-red-600' : 'bg-blue-100 text-blue-600'}`}>
              <Monitor className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-lg font-bold text-slate-800">Berbagi Layar Admin</h3>
              <p className="text-sm text-slate-500">Tampilkan layar Anda ke semua siswa yang sedang login</p>
            </div>
          </div>
          <div className="flex items-center space-x-2 text-sm text-slate-500">
            <Users className="w-4 h-4" />
            <span>{clientCount} siswa online</span>
          </div>
        </div>

        {error && (
          <div className="mb-4 flex items-center space-x-2 text-sm text-red-600 bg-red-50 border border-red-200 rounded-xl px-4 py-3">
            <AlertCircle className="w-4 h-4 flex-shrink-0" />
            <span>{error}</span>
          </div>
        )}

        <div className="flex items-center space-x-4">
          {!sharing ? (
            <button
              onClick={startSharing}
              disabled={!socket || clientCount === 0}
              className="flex items-center space-x-2 px-6 py-3 bg-blue-600 hover:bg-blue-700 disabled:bg-slate-300 disabled:cursor-not-allowed text-white rounded-xl font-medium transition-colors"
            >
              <Monitor className="w-5 h-5" />
              <span>Mulai Berbagi Layar</span>
            </button>
          ) : (
            <button
              onClick={stopSharing}
              className="flex items-center space-x-2 px-6 py-3 bg-red-600 hover:bg-red-700 text-white rounded-xl font-medium transition-colors animate-pulse"
            >
              <MonitorOff className="w-5 h-5" />
              <span>Hentikan Berbagi</span>
            </button>
          )}

          {sharing && (
            <div className="flex items-center space-x-2 text-sm text-emerald-600 font-medium">
              <span className="w-2.5 h-2.5 rounded-full bg-emerald-500 animate-pulse" />
              <span>Sedang berbagi ke {clientCount} siswa</span>
            </div>
          )}

          {!socket && (
            <p className="text-sm text-slate-400">Menunggu koneksi socket...</p>
          )}
          {socket && clientCount === 0 && !sharing && (
            <p className="text-sm text-slate-400">Tidak ada siswa yang sedang online</p>
          )}
        </div>

        {/* Preview */}
        {sharing && (
          <div className="mt-6">
            <p className="text-xs font-semibold text-slate-500 uppercase tracking-wider mb-2">Pratinjau Layar Anda</p>
            <div className="relative bg-slate-950 rounded-xl overflow-hidden aspect-video max-w-2xl">
              <video
                ref={previewRef}
                autoPlay
                muted
                className="w-full h-full object-contain"
              />
              <div className="absolute top-2 right-2 flex items-center space-x-1.5 bg-red-600 text-white text-xs font-bold px-2.5 py-1 rounded-full">
                <span className="w-1.5 h-1.5 rounded-full bg-white animate-pulse" />
                <span>LIVE</span>
              </div>
            </div>
          </div>
        )}

        {/* Hidden canvas for frame capture */}
        <canvas ref={canvasRef} className="hidden" />
      </div>

      <div className="bg-blue-50 border border-blue-200 rounded-2xl p-5">
        <p className="text-sm font-semibold text-blue-800 mb-2">Cara Kerja:</p>
        <ul className="text-sm text-blue-700 space-y-1 list-disc list-inside">
          <li>Klik "Mulai Berbagi Layar" lalu pilih jendela atau layar yang ingin dibagikan</li>
          <li>Layar Anda akan tampil secara real-time di semua PC siswa yang sedang login</li>
          <li>Siswa tidak bisa menutup tampilan ini selama admin masih berbagi</li>
          <li>Klik "Hentikan Berbagi" untuk mengakhiri sesi berbagi layar</li>
        </ul>
      </div>
    </div>
  );
}
