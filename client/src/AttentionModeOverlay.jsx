import React, { useEffect, useState } from 'react';
import { AlertCircle, Eye, EyeOff } from 'lucide-react';

/**
 * AttentionModeOverlay
 * 
 * Fullscreen overlay yang muncul saat admin mengaktifkan Attention Mode.
 * Features:
 * - Fullscreen black/blur overlay
 * - Block semua keyboard & mouse input
 * - Tampilkan pesan dari admin
 * - Tidak bisa ditutup sampai admin menonaktifkan
 */
export default function AttentionModeOverlay({ enabled, message, onAcknowledge }) {
  const [localEnabled, setLocalEnabled] = useState(enabled);
  const [fadeOut, setFadeOut] = useState(false);

  useEffect(() => {
    if (enabled) {
      setLocalEnabled(true);
      setFadeOut(false);
    } else if (localEnabled) {
      // Fade out animation sebelum unmount
      setFadeOut(true);
      const timer = setTimeout(() => {
        setLocalEnabled(false);
        setFadeOut(false);
      }, 300);
      return () => clearTimeout(timer);
    }
  }, [enabled, localEnabled]);

  useEffect(() => {
    if (!localEnabled) return;

    // Block keyboard events
    const blockKeyboard = (e) => {
      // Allow Ctrl+Alt+Q untuk admin exit
      if (e.ctrlKey && e.altKey && e.key.toLowerCase() === 'q') {
        return;
      }
      e.preventDefault();
      e.stopPropagation();
      return false;
    };

    // Block context menu
    const blockContextMenu = (e) => {
      e.preventDefault();
      return false;
    };

    // Block mouse events (selain movement)
    const blockMouse = (e) => {
      if (e.type !== 'mousemove') {
        e.preventDefault();
        e.stopPropagation();
        return false;
      }
    };

    // Add event listeners with capture phase
    document.addEventListener('keydown', blockKeyboard, true);
    document.addEventListener('keyup', blockKeyboard, true);
    document.addEventListener('keypress', blockKeyboard, true);
    document.addEventListener('contextmenu', blockContextMenu, true);
    document.addEventListener('mousedown', blockMouse, true);
    document.addEventListener('mouseup', blockMouse, true);
    document.addEventListener('click', blockMouse, true);
    document.addEventListener('dblclick', blockMouse, true);

    // Send acknowledgement to admin
    if (onAcknowledge) {
      onAcknowledge();
    }

    return () => {
      document.removeEventListener('keydown', blockKeyboard, true);
      document.removeEventListener('keyup', blockKeyboard, true);
      document.removeEventListener('keypress', blockKeyboard, true);
      document.removeEventListener('contextmenu', blockContextMenu, true);
      document.removeEventListener('mousedown', blockMouse, true);
      document.removeEventListener('mouseup', blockMouse, true);
      document.removeEventListener('click', blockMouse, true);
      document.removeEventListener('dblclick', blockMouse, true);
    };
  }, [localEnabled, onAcknowledge]);

  if (!localEnabled) return null;

  return (
    <div
      className={`fixed inset-0 z-[9999] flex flex-col items-center justify-center transition-opacity duration-300 ${
        fadeOut ? 'opacity-0' : 'opacity-100'
      }`}
      style={{
        background: 'linear-gradient(135deg, rgba(15, 23, 42, 0.98) 0%, rgba(30, 41, 59, 0.98) 100%)',
        backdropFilter: 'blur(20px)',
        WebkitBackdropFilter: 'blur(20px)',
      }}
    >
      {/* Animated background pattern */}
      <div className="absolute inset-0 opacity-10">
        <div className="absolute inset-0" style={{
          backgroundImage: 'radial-gradient(circle at 20% 50%, rgba(59, 130, 246, 0.3) 0%, transparent 50%), radial-gradient(circle at 80% 80%, rgba(99, 102, 241, 0.3) 0%, transparent 50%)',
          animation: 'pulse 4s ease-in-out infinite',
        }} />
      </div>

      {/* Main content */}
      <div className="relative z-10 text-center px-8 max-w-2xl animate-in zoom-in-95 duration-500">
        {/* Icon */}
        <div className="mb-8 flex justify-center">
          <div className="relative">
            <div className="absolute inset-0 bg-blue-500/20 rounded-full animate-ping" />
            <div className="relative bg-gradient-to-br from-blue-500 to-indigo-600 p-6 rounded-full shadow-2xl">
              <Eye className="w-16 h-16 text-white" />
            </div>
          </div>
        </div>

        {/* Message */}
        <h1 className="text-4xl md:text-5xl font-black text-white mb-4 tracking-tight">
          PERHATIAN
        </h1>
        <div className="bg-white/10 backdrop-blur-md rounded-2xl p-6 mb-8 border border-white/20 shadow-xl">
          <p className="text-xl md:text-2xl text-white font-medium leading-relaxed">
            {message || 'Mohon perhatian ke instruktur'}
          </p>
        </div>

        {/* Status indicator */}
        <div className="flex items-center justify-center space-x-3 text-blue-300">
          <div className="flex items-center space-x-2">
            <div className="w-3 h-3 bg-blue-400 rounded-full animate-pulse shadow-lg shadow-blue-400/50" />
            <span className="text-sm font-medium">Layar dikunci oleh instruktur</span>
          </div>
        </div>

        {/* Additional info */}
        <div className="mt-12 text-xs text-slate-400 space-y-1">
          <p>Keyboard dan mouse dinonaktifkan sementara</p>
          <p className="flex items-center justify-center space-x-1">
            <EyeOff className="w-3 h-3" />
            <span>Menunggu instruksi dari instruktur...</span>
          </p>
        </div>
      </div>

      {/* Bottom bar */}
      <div className="absolute bottom-0 left-0 right-0 h-1 bg-gradient-to-r from-blue-500 via-indigo-500 to-purple-500">
        <div className="h-full bg-white/30 animate-pulse" />
      </div>

      {/* CSS for animations */}
      <style>{`
        @keyframes pulse {
          0%, 100% { opacity: 0.1; transform: scale(1); }
          50% { opacity: 0.3; transform: scale(1.05); }
        }
      `}</style>
    </div>
  );
}
