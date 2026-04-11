import React, { useState, useEffect, useRef } from 'react';
import { MessageCircle, Send, X, ChevronDown } from 'lucide-react';

/**
 * ChatBubble - Floating chat notification for students (client side)
 * Receives broadcast messages from admin and allows replies
 */
export default function ChatBubble({ socket, studentName, pcName }) {
  const [messages, setMessages] = useState([]);
  const [isOpen, setIsOpen] = useState(false);
  const [reply, setReply] = useState('');
  const [unread, setUnread] = useState(0);
  const messagesEndRef = useRef(null);

  // Listen for admin messages
  useEffect(() => {
    if (!socket) return;

    const handler = (data) => {
      const msg = {
        id: data.id || Date.now(),
        from: 'Admin',
        message: data.message,
        timestamp: data.timestamp || new Date().toISOString(),
        type: 'received',
      };
      setMessages((prev) => [...prev, msg]);
      if (!isOpen) {
        setUnread((n) => n + 1);
      }
    };

    socket.on('chat:message-from-admin', handler);
    return () => socket.off('chat:message-from-admin', handler);
  }, [socket, isOpen]);

  // Auto-scroll to bottom
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  // Reset unread when opening
  useEffect(() => {
    if (isOpen) setUnread(0);
  }, [isOpen]);

  const handleSend = () => {
    if (!reply.trim() || !socket) return;

    socket.emit('chat:reply-to-admin', {
      message: reply.trim(),
      student_name: studentName,
      timestamp: new Date().toISOString(),
    });

    setMessages((prev) => [
      ...prev,
      {
        id: Date.now(),
        from: 'Saya',
        message: reply.trim(),
        timestamp: new Date().toISOString(),
        type: 'sent',
      },
    ]);
    setReply('');
  };

  const handleKeyDown = (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const formatTime = (ts) => {
    const d = new Date(ts);
    return d.toLocaleTimeString('id-ID', { hour: '2-digit', minute: '2-digit' });
  };

  // Don't render if no messages and not open
  if (messages.length === 0 && !isOpen) return null;

  // Floating bubble (collapsed)
  if (!isOpen) {
    return (
      <button
        onClick={() => setIsOpen(true)}
        className="fixed bottom-20 right-4 z-[9998] w-12 h-12 bg-blue-600 hover:bg-blue-700 text-white rounded-full shadow-xl flex items-center justify-center transition-all duration-200 hover:scale-110 animate-bounce"
        title="Pesan dari Admin"
      >
        <MessageCircle className="w-5 h-5" />
        {unread > 0 && (
          <span className="absolute -top-1 -right-1 w-5 h-5 bg-red-500 text-white text-[10px] font-bold rounded-full flex items-center justify-center">
            {unread}
          </span>
        )}
      </button>
    );
  }

  // Chat panel (expanded)
  return (
    <div className="fixed bottom-4 right-4 z-[9998] w-80 max-h-[420px] bg-white rounded-xl shadow-2xl border border-slate-200 flex flex-col overflow-hidden">
      {/* Header */}
      <div className="bg-blue-600 text-white px-4 py-3 flex items-center justify-between shrink-0">
        <div className="flex items-center space-x-2">
          <MessageCircle className="w-4 h-4" />
          <span className="font-semibold text-sm">Pesan dari Admin</span>
        </div>
        <div className="flex items-center space-x-1">
          <button
            onClick={() => setIsOpen(false)}
            className="p-1 hover:bg-white/20 rounded transition-colors"
          >
            <ChevronDown className="w-4 h-4" />
          </button>
          <button
            onClick={() => { setIsOpen(false); setMessages([]); setUnread(0); }}
            className="p-1 hover:bg-white/20 rounded transition-colors"
            title="Tutup & Hapus Pesan"
          >
            <X className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Messages */}
      <div className="flex-1 overflow-y-auto p-3 space-y-2 bg-slate-50 max-h-[280px]">
        {messages.map((msg) => (
          <div
            key={msg.id}
            className={`flex ${msg.type === 'sent' ? 'justify-end' : 'justify-start'}`}
          >
            <div
              className={`max-w-[85%] rounded-lg px-3 py-2 ${
                msg.type === 'sent'
                  ? 'bg-blue-500 text-white'
                  : 'bg-white border border-slate-200 text-slate-800'
              }`}
            >
              {msg.type === 'received' && (
                <p className="text-[10px] font-bold text-blue-600 mb-0.5">{msg.from}</p>
              )}
              <p className="text-sm whitespace-pre-wrap break-words">{msg.message}</p>
              <p className={`text-[10px] mt-1 ${msg.type === 'sent' ? 'text-blue-100' : 'text-slate-400'}`}>
                {formatTime(msg.timestamp)}
              </p>
            </div>
          </div>
        ))}
        <div ref={messagesEndRef} />
      </div>

      {/* Reply input */}
      <div className="p-2 border-t border-slate-200 bg-white shrink-0">
        <div className="flex items-center space-x-2">
          <input
            type="text"
            value={reply}
            onChange={(e) => setReply(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Balas pesan..."
            className="flex-1 px-3 py-2 border border-slate-300 rounded-lg text-sm focus:ring-1 focus:ring-blue-500 focus:border-blue-500 outline-none"
          />
          <button
            onClick={handleSend}
            disabled={!reply.trim()}
            className="p-2 bg-blue-600 hover:bg-blue-700 disabled:bg-slate-300 text-white rounded-lg transition-colors"
          >
            <Send className="w-4 h-4" />
          </button>
        </div>
      </div>
    </div>
  );
}
