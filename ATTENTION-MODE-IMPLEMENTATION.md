# Attention Mode Implementation Guide

## ✅ Yang Sudah Dikerjakan

### 1. Server-Side Handler (server/src/realtimeHub.js) ✅
```javascript
// Event handler untuk admin mengirim attention mode
socket.on('admin:attention-mode', ({ enabled, message, target } = {}) => {
  const payload = {
    enabled: Boolean(enabled),
    message: message || 'Mohon perhatian ke instruktur',
    timestamp: Date.now(),
  };

  if (target && target !== 'all') {
    // Send to specific PC
    const targetRoom = getClientRoom(target);
    if (targetRoom) {
      io.to(targetRoom).emit('attention-mode', payload);
    }
  } else {
    // Broadcast to all clients
    io.emit('attention-mode', payload);
  }

  // Notify other admins
  socket.to('admins').emit('attention-mode-status', {
    ...payload,
    target: target || 'all',
    admin_id: socket.id,
  });
});

// Client acknowledgement
socket.on('client:attention-ack', (payload = {}) => {
  const pcName = normalizePcName(socket.data.pc_name);
  if (!pcName) return;
  
  io.to('admins').emit('client:attention-ack', {
    pc_name: pcName,
    acknowledged: true,
    timestamp: Date.now(),
  });
});
```

### 2. Client-Side Overlay Component (client/src/AttentionModeOverlay.jsx) ✅
- Fullscreen overlay dengan blur background
- Block semua keyboard & mouse input (kecuali Ctrl+Alt+Q)
- Tampilkan pesan dari admin
- Fade in/out animation
- Send acknowledgement ke admin

## 🔧 Langkah Implementasi Selanjutnya

### Step 1: Integrasikan ke client/src/App.jsx

Tambahkan import dan state di bagian atas App.jsx:

```javascript
import AttentionModeOverlay from './AttentionModeOverlay.jsx';
import { io } from 'socket.io-client';

// Di dalam component App()
const [attentionMode, setAttentionMode] = useState({ enabled: false, message: '' });
const socketRef = useRef(null);
```

Tambahkan socket.io connection setelah login berhasil:

```javascript
// Di dalam useEffect, setelah serverUrl dan mode berubah
useEffect(() => {
  if (!serverUrl || mode === MODE_LOADING || mode === MODE_SETUP || mode === MODE_LOGIN) {
    // Disconnect socket jika belum login
    if (socketRef.current) {
      socketRef.current.disconnect();
      socketRef.current = null;
    }
    return;
  }

  // Connect socket setelah login
  const socket = io(serverUrl, {
    transports: ['websocket', 'polling'],
    auth: { role: 'client' },
  });

  socketRef.current = socket;

  // Listen for attention mode from admin
  socket.on('attention-mode', (payload) => {
    setAttentionMode({
      enabled: payload.enabled,
      message: payload.message || 'Mohon perhatian ke instruktur',
    });
  });

  socket.on('connect', () => {
    console.log('[Socket] Connected to server');
  });

  socket.on('disconnect', () => {
    console.log('[Socket] Disconnected from server');
    setAttentionMode({ enabled: false, message: '' });
  });

  return () => {
    socket.disconnect();
    socketRef.current = null;
  };
}, [serverUrl, mode]);
```

Tambahkan overlay di return statement (di luar conditional rendering):

```javascript
return (
  <>
    {/* Attention Mode Overlay - Always on top */}
    <AttentionModeOverlay
      enabled={attentionMode.enabled}
      message={attentionMode.message}
      onAcknowledge={() => {
        if (socketRef.current) {
          socketRef.current.emit('client:attention-ack', {});
        }
      }}
    />

    {/* Rest of your app */}
    {mode === MODE_LOADING && (...)}
    {mode === MODE_SETUP && (...)}
    {/* ... */}
  </>
);
```

### Step 2: Tambahkan Admin UI Controls

Di admin/src/AdminDashboard.jsx, tambahkan state dan handlers:

```javascript
// Di dalam AdminDashboard component
const [attentionModeActive, setAttentionModeActive] = useState(false);
const [attentionMessage, setAttentionMessage] = useState('Mohon perhatian ke instruktur');
const [showAttentionDialog, setShowAttentionDialog] = useState(false);

// Handler untuk toggle attention mode
const handleAttentionMode = (enabled, customMessage = null) => {
  if (!realtimeSocketRef.current) return;

  const message = customMessage || attentionMessage;
  realtimeSocketRef.current.emit('admin:attention-mode', {
    enabled,
    message,
    target: 'all',  // atau specific PC
  });

  setAttentionModeActive(enabled);
  setShowAttentionDialog(false);
  showToast(
    enabled
      ? 'Attention Mode diaktifkan - Layar semua client dikunci'
      : 'Attention Mode dinonaktifkan - Client dapat melanjutkan'
  );
};

// Listen for attention mode status from other admins
useEffect(() => {
  if (!realtimeSocketRef.current) return;

  realtimeSocketRef.current.on('attention-mode-status', (payload) => {
    setAttentionModeActive(payload.enabled);
  });

  return () => {
    realtimeSocketRef.current?.off('attention-mode-status');
  };
}, []);
```

Tambahkan button di toolbar monitoring (di dalam `renderMonitoring()`):

```javascript
// Di toolbar monitoring, setelah tombol "Kunci Semua PC"
<button
  onClick={() => {
    if (attentionModeActive) {
      handleAttentionMode(false);
    } else {
      setShowAttentionDialog(true);
    }
  }}
  className={`px-4 py-2 border rounded-xl text-sm font-medium flex items-center space-x-2 transition-colors ${
    attentionModeActive
      ? 'bg-emerald-50 border-emerald-200 text-emerald-700 hover:bg-emerald-100'
      : 'bg-amber-50 border-amber-200 text-amber-700 hover:bg-amber-100'
  }`}
>
  {attentionModeActive ? (
    <>
      <EyeOff className="w-4 h-4" />
      <span>Nonaktifkan Attention Mode</span>
    </>
  ) : (
    <>
      <Eye className="w-4 h-4" />
      <span>Aktifkan Attention Mode</span>
    </>
  )}
</button>
```

Tambahkan dialog untuk custom message (sebelum closing tag component):

```javascript
{/* ─── MODAL: Attention Mode Settings ───────────────────────── */}
{showAttentionDialog && (
  <div className="fixed inset-0 bg-slate-900/50 backdrop-blur-sm z-50 flex items-center justify-center p-4 animate-in fade-in">
    <div className="bg-white rounded-2xl shadow-xl max-w-md w-full p-6 animate-in zoom-in-95 duration-500">
      <div className="flex items-center justify-center w-16 h-16 rounded-full bg-amber-100 text-amber-600 mx-auto mb-4">
        <Eye className="w-8 h-8" />
      </div>
      <h3 className="text-xl font-bold text-center text-slate-800 mb-2">
        Aktifkan Attention Mode
      </h3>
      <p className="text-center text-slate-500 mb-5 text-sm">
        Layar semua siswa akan dikunci dan menampilkan pesan berikut:
      </p>
      <textarea
        value={attentionMessage}
        onChange={(e) => setAttentionMessage(e.target.value)}
        rows={3}
        className="w-full px-4 py-3 border border-slate-300 rounded-xl focus:ring-2 focus:ring-amber-500 outline-none resize-none text-sm"
        placeholder="Masukkan pesan untuk siswa..."
      />
      <div className="flex space-x-3 mt-5">
        <button
          onClick={() => setShowAttentionDialog(false)}
          className="flex-1 py-2.5 bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 rounded-xl font-medium transition-colors"
        >
          Batal
        </button>
        <button
          onClick={() => handleAttentionMode(true)}
          className="flex-1 py-2.5 bg-amber-600 hover:bg-amber-700 text-white rounded-xl font-medium transition-colors shadow-lg shadow-amber-600/20"
        >
          Aktifkan Sekarang
        </button>
      </div>
    </div>
  </div>
)}
```

### Step 3: Install Dependencies

Client membutuhkan socket.io-client:

```bash
cd client
npm install socket.io-client
```

### Step 4: Test

1. Buka Admin Dashboard
2. Klik tombol "Aktifkan Attention Mode" di tab Monitoring
3. Isi custom message (opsional)
4. Klik "Aktifkan Sekarang"
5. Cek client PC - layar harus tertutup overlay dengan message
6. Klik "Nonaktifkan Attention Mode" untuk melepas lock

## 🎯 Fitur yang Sudah Diimplementasikan

### Client Side:
✅ Fullscreen overlay dengan blur background  
✅ Block keyboard (kecuali Ctrl+Alt+Q emergency exit)  
✅ Block mouse input (klik, double-click, right-click)  
✅ Tampilan pesan custom dari admin  
✅ Animated icons & status indicators  
✅ Fade in/out transitions  
✅ Acknowledgement ke admin  

### Server Side:
✅ WebSocket handler untuk broadcast ke semua/specific client  
✅ Room-based targeting (bisa ke specific PC)  
✅ Notify other admins tentang status  
✅ Acknowledgement tracking  

### Admin Side:
⏳ Toggle button di monitoring dashboard  
⏳ Custom message dialog  
⏳ Target selection (all/specific PC)  
⏳ Acknowledgement indicators  

## 🚀 Enhancement Ideas (Future)

1. **Timer Mode**: Auto-disable setelah X menit
2. **Break Timer**: Countdown timer untuk break time
3. **Quick Messages**: Preset messages untuk situasi umum
4. **Per-PC Control**: Lock specific students only
5. **Attention Level**: Warning (yellow) vs Critical (red)
6. **Sound Alert**: Play sound saat attention mode aktif
7. **Statistics**: Track berapa lama attention mode aktif
8. **History Log**: Simpan log kapan attention mode digunakan

## 📝 Testing Checklist

- [ ] Attention mode aktif di semua client saat broadcast
- [ ] Keyboard benar-benar ter-block (test Alt+Tab, Windows key, dll)
- [ ] Mouse ter-block (kecuali movement)
- [ ] Ctrl+Alt+Q tetap berfungsi untuk emergency exit
- [ ] Custom message tampil dengan benar
- [ ] Fade in/out animation smooth
- [ ] Acknowledgement terkirim ke admin
- [ ] Multiple admins bisa lihat status yang sama
- [ ] Attention mode auto-release saat client disconnect/reconnect
- [ ] Performance OK saat banyak client (load testing)

## 🐛 Known Issues & Limitations

1. **Alt+Tab**: Electron tidak bisa 100% block Alt+Tab di Windows, tapi recovery mechanism sudah ada
2. **Task Manager**: Ctrl+Shift+Esc sudah di-block, tapi bisa dibuka via Ctrl+Alt+Del
3. **Virtual Desktops**: Windows 10+ virtual desktop bisa bypass, tapi recovery akan restore
4. **Second Monitor**: Jika ada monitor kedua, user masih bisa lihat (limitation Electron)

## 💡 Tips Penggunaan

1. **Gunakan untuk**:
   - Penjelasan materi penting
   - Demonstrasi ke semua siswa
   - Break time
   - Emergency announcement
   - Pause saat troubleshooting

2. **Jangan gunakan untuk**:
   - Durasi sangat lama (>10 menit) - siswa bisa frustasi
   - Saat ujian (gunakan mode logout saja)
   - Tanpa komunikasi verbal - selalu berikan context

3. **Best Practices**:
   - Berikan warning sebelum activate
   - Gunakan message yang jelas dan singkat
   - Jangan lupa deactivate setelah selesai
   - Komunikasi via voice/projector juga penting
