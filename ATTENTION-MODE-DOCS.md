# 🔔 Attention Mode - Dokumentasi Lengkap

## Gambaran Umum

**Attention Mode** adalah fitur yang memungkinkan instruktur untuk menarik perhatian **semua siswa** secara serentak dengan menampilkan overlay fullscreen di setiap PC client yang sedang login. Fitur ini sangat berguna saat instruktur perlu memberikan pengumuman penting atau instruksi yang harus diperhatikan semua siswa.

---

## ✨ Fitur Utama

### 1. **Broadcast Realtime**
- Overlay muncul **seketika** di semua PC client yang aktif
- Menggunakan Socket.IO untuk komunikasi realtime
- Tidak memerlukan refresh browser

### 2. **Overlay Fullscreen dengan Blur Effect**
- Layar siswa akan diblur dengan backdrop-filter CSS
- Overlay tidak bisa di-bypass atau ditutup tanpa klik tombol
- Desain modern dengan animasi smooth

### 3. **Custom Message**
- Instruktur bisa mengetik pesan khusus
- Pesan default: "Mohon perhatian ke instruktur"
- Karakter unlimited untuk pesan panjang

### 4. **Visual Attention-Grabbing**
- Animasi pulse pada ikon bell
- Gradient background oranye-kuning yang eye-catching
- Typography besar dan bold untuk keterbacaan

### 5. **User Acknowledgement**
- Siswa harus klik tombol "Saya Mengerti" untuk menutup
- Memberikan konfirmasi bahwa siswa sudah membaca pesan
- Tombol dengan hover effect untuk interaktivitas

---

## 🎯 Cara Penggunaan

### Dari Admin Dashboard:

1. **Login ke Admin Dashboard**
   - Buka aplikasi LabKom Admin
   - Masukkan password admin

2. **Navigasi ke Tab "Kontrol & Kebijakan"**
   - Klik menu "Kontrol & Kebijakan" di sidebar

3. **Scroll ke Modul "Attention Mode"**
   - Terletak di bagian bawah halaman
   - Icon bell oranye

4. **Aktifkan Mode Perhatian:**
   
   **Opsi A: Dengan Pesan Custom**
   ```
   1. Ketik pesan di textarea
      Contoh: "Mohon perhatian! Saya akan menjelaskan materi penting."
   2. Klik tombol "Aktifkan Mode Perhatian untuk Semua Client"
   3. Overlay akan muncul di semua PC client
   ```

   **Opsi B: Tanpa Pesan (Default)**
   ```
   1. Biarkan textarea kosong
   2. Klik tombol "Aktifkan Mode Perhatian untuk Semua Client"
   3. Pesan default akan ditampilkan: "Mohon perhatian ke instruktur"
   ```

5. **Monitor Status Aktif:**
   - Saat aktif, akan muncul panel status dengan border oranye
   - Menampilkan pesan yang sedang ditampilkan
   - Dot animasi pulse menunjukkan mode aktif

6. **Nonaktifkan Mode:**
   - Klik tombol "Nonaktifkan Mode Perhatian"
   - Overlay akan hilang dari semua PC client seketika

---

## 🖥️ Pengalaman Siswa

Ketika Attention Mode diaktifkan:

1. **Overlay Muncul Fullscreen**
   - Layar diblur dengan efek backdrop
   - Tidak bisa klik area lain
   - Fokus penuh pada pesan

2. **Konten yang Ditampilkan:**
   - Icon bell besar dengan animasi pulse
   - Judul: "PERHATIAN!"
   - Pesan dari instruktur (atau pesan default)
   - Tombol "Saya Mengerti"

3. **Cara Menutup:**
   - Siswa harus membaca pesan
   - Klik tombol "Saya Mengerti"
   - Overlay akan hilang dan siswa bisa melanjutkan aktivitas

---

## 🔧 Arsitektur Teknis

### Server-Side (`server/src/realtimeHub.js`)

```javascript
// Event handler untuk broadcast dari admin
socket.on('admin:attention-mode', ({ enabled, message }) => {
  // Broadcast ke semua client yang sedang login
  io.to('clients').emit('attention:mode', { enabled, message });
});
```

### API Endpoint (`server/src/routes/attentionRoutes.js`)

```javascript
POST /api/attention/broadcast
Body: {
  enabled: boolean,
  message: string
}

Response: {
  success: true,
  message: "Attention mode broadcasted"
}
```

### Client-Side Socket Listener (`client/src/App.jsx`)

```javascript
useEffect(() => {
  if (!socket) return;
  
  socket.on('attention:mode', ({ enabled, message }) => {
    if (enabled) {
      setAttentionMode({ show: true, message });
    } else {
      setAttentionMode({ show: false, message: '' });
    }
  });
}, [socket]);
```

### Overlay Component (`client/src/AttentionModeOverlay.jsx`)

- React component dengan state management
- Backdrop blur CSS untuk efek visual
- Click handler untuk tombol acknowledgement
- Animasi dengan Tailwind CSS

### Admin UI (`admin/src/AdminDashboard.jsx`)

- State management untuk enable/disable
- Form input untuk custom message
- API call untuk broadcast
- Status monitoring panel

---

## 📊 Flow Diagram

```
┌─────────────────┐
│  Admin Dashboard│
│  (Kontrol Tab)  │
└────────┬────────┘
         │
         │ 1. Admin mengaktifkan Attention Mode
         │    dengan/tanpa custom message
         ▼
┌─────────────────┐
│  POST Request   │
│  /api/attention │
│  /broadcast     │
└────────┬────────┘
         │
         │ 2. Server menerima request
         │    dan emit socket event
         ▼
┌─────────────────┐
│  Socket.IO Hub  │
│  Realtime Event │
└────────┬────────┘
         │
         │ 3. Broadcast 'attention:mode' event
         │    ke semua client yang login
         ▼
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Client PC #1   │     │  Client PC #2   │ ... │  Client PC #N   │
│  (Siswa Login)  │     │  (Siswa Login)  │     │  (Siswa Login)  │
└────────┬────────┘     └────────┬────────┘     └────────┬────────┘
         │                       │                       │
         │ 4. Socket listener menerima event
         │    dan update state attentionMode
         ▼                       ▼                       ▼
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Overlay Muncul │     │  Overlay Muncul │     │  Overlay Muncul │
│  dengan Message │     │  dengan Message │     │  dengan Message │
└────────┬────────┘     └────────┬────────┘     └────────┬────────┘
         │                       │                       │
         │ 5. Siswa klik "Saya Mengerti"
         │    Overlay hilang (local state)
         ▼
   Kembali ke sesi normal
```

---

## 🎨 Desain Visual

### Color Palette:
- **Background Gradient**: `from-orange-500 via-amber-500 to-yellow-400`
- **Text Color**: `text-white` (kontras tinggi)
- **Button Hover**: `from-slate-700 to-slate-800`
- **Icon Color**: `text-orange-500` (animated pulse)

### Typography:
- **Heading**: `text-5xl font-black` (sangat besar dan tebal)
- **Message**: `text-xl font-medium` (mudah dibaca)
- **Button**: `text-lg font-bold` (jelas dan actionable)

### Animations:
- **Bell Icon**: `animate-bounce` dengan interval
- **Pulse Effect**: `animate-pulse` untuk attention grabbing
- **Fade In**: Smooth transition saat muncul
- **Scale Effect**: Slight scale pada hover button

---

## ⚙️ Konfigurasi & Customization

### Mengubah Pesan Default

Edit di `server/src/routes/attentionRoutes.js`:
```javascript
const defaultMessage = 'Mohon perhatian ke instruktur'; // Ubah di sini
```

### Mengubah Warna Tema

Edit di `client/src/AttentionModeOverlay.jsx`:
```jsx
// Ganti gradient background
className="bg-gradient-to-br from-orange-500 via-amber-500 to-yellow-400"
// Menjadi warna lain, contoh:
className="bg-gradient-to-br from-blue-500 via-purple-500 to-pink-400"
```

### Menambah Sound Effect (Opsional)

Tambahkan di `client/src/AttentionModeOverlay.jsx`:
```javascript
useEffect(() => {
  if (show) {
    const audio = new Audio('/sounds/notification.mp3');
    audio.play();
  }
}, [show]);
```

---

## 🔒 Security & Best Practices

### Authentication
- ✅ Hanya admin yang terautentikasi bisa broadcast
- ✅ Token JWT divalidasi di server sebelum emit event
- ✅ Socket auth memverifikasi role admin

### Rate Limiting
```javascript
// Implementasi di server (opsional untuk production)
const rateLimit = require('express-rate-limit');

const attentionLimiter = rateLimit({
  windowMs: 60 * 1000, // 1 menit
  max: 5, // Max 5 request per menit
  message: 'Terlalu banyak broadcast, coba lagi nanti'
});

router.post('/broadcast', attentionLimiter, broadcastAttentionMode);
```

### Input Validation
- ✅ Message di-sanitize untuk mencegah XSS
- ✅ Max length validation untuk message
- ✅ Boolean validation untuk enabled flag

---

## 🐛 Troubleshooting

### Issue: Overlay tidak muncul di client

**Penyebab Umum:**
1. Client tidak terkoneksi ke socket server
2. Client belum login (not authenticated)
3. Network firewall memblokir WebSocket

**Solusi:**
```javascript
// Di client, cek koneksi socket
console.log('Socket connected:', socket?.connected);
console.log('Socket ID:', socket?.id);

// Di server, cek emit event
console.log('Broadcasting to clients:', io.sockets.adapter.rooms.get('clients'));
```

### Issue: Overlay tidak hilang setelah dinonaktifkan

**Solusi:**
```javascript
// Pastikan socket listener aktif
socket.on('attention:mode', ({ enabled, message }) => {
  console.log('Received attention mode:', enabled, message);
  // ... state update
});
```

### Issue: Message tidak tampil dengan benar

**Solusi:**
```javascript
// Cek encoding di server
const message = req.body.message || 'Mohon perhatian ke instruktur';
console.log('Message to broadcast:', message);
```

---

## 📈 Use Cases

### 1. Pengumuman Penting
```
"Ujian akan dimulai dalam 5 menit. Siapkan alat tulis dan tutup semua aplikasi."
```

### 2. Instruksi Mendadak
```
"STOP! Jangan ketik apa-apa. Dengarkan penjelasan saya terlebih dahulu."
```

### 3. Emergency Alert
```
"EVAKUASI! Segera keluar ruangan dengan tertib mengikuti jalur evakuasi."
```

### 4. Break Time
```
"Istirahat 10 menit. Silakan keluar ruangan. Jangan sentuh komputer."
```

### 5. Transisi Aktivitas
```
"Selesaikan tugas yang sedang dikerjakan. Kita akan pindah ke materi berikutnya."
```

---

## 🚀 Future Enhancements

### Planned Features:
- [ ] **Sound Notification**: Play audio saat overlay muncul
- [ ] **Priority Levels**: Warning (yellow), Danger (red), Info (blue)
- [ ] **Auto-dismiss Timer**: Overlay hilang otomatis setelah X detik
- [ ] **Acknowledgement Tracking**: Log siswa mana yang sudah klik "Mengerti"
- [ ] **Scheduled Attention**: Atur waktu kapan broadcast akan aktif
- [ ] **Template Messages**: Preset pesan untuk situasi umum
- [ ] **Multi-language Support**: Bahasa Indonesia & English
- [ ] **Image/Video Support**: Tampilkan media dalam overlay

---

## 📝 Changelog

### Version 1.0.0 (Current)
- ✅ Initial implementation
- ✅ Realtime broadcast via Socket.IO
- ✅ Custom message support
- ✅ Fullscreen overlay with blur effect
- ✅ Admin dashboard controls
- ✅ User acknowledgement button

---

## 👥 Support & Contributing

Untuk pertanyaan atau issue:
1. Buka file ini dan baca troubleshooting
2. Cek console log di browser & server
3. Periksa koneksi Socket.IO

**Developed with ❤️ for LabKom System**
