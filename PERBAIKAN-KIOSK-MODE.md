# Perbaikan Kiosk Mode - Client Application

## Tanggal: 1 April 2026

## Masalah yang Diperbaiki

### 1. **Alt+Tab Bypass di Mode Lock**
**Masalah**: Client masih bisa menggunakan Alt+Tab untuk berpindah window meskipun dalam mode kiosk/lock untuk mengisi password dan username.

**Solusi yang Diterapkan**:
- **Focus Recovery yang Lebih Agresif**: Mengurangi delay recovery dari 30ms menjadi 10ms saat dalam mode kiosk lock
- **Deteksi Mode Lock**: Menambahkan fungsi `isKioskLocked()` untuk membedakan kapan window dalam mode lock
- **Recovery Berlapis**:
  - Event `blur`: Recovery 10ms (mode lock) vs 30ms (mode widget)
  - Event `minimize`: Recovery 5ms dengan `event.preventDefault()`
  - Event `hide`: Recovery 5ms
  - Event `restore`: Recovery 10ms
  - Event `browser-window-blur`: Recovery 10ms (mode lock) vs 150ms (mode widget)
  
- **Penguatan Focus Mechanism**:
  - Menambahkan event listener `focus` untuk memastikan kiosk mode tetap aktif
  - Menambahkan `mainWindow.blur()` lalu `mainWindow.focus()` di Windows untuk force refresh focus
  - Conditional prevention pada `will-resize` dan `move` - hanya mencegah jika dalam mode lock

**Catatan Penting**:
> Electron tidak bisa 100% memblokir Alt+Tab di Windows karena keterbatasan OS. Yang dilakukan adalah **recovery sangat cepat** (10ms) sehingga window kiosk langsung kembali fokus. User masih bisa melihat sekilas window lain tapi tidak bisa berinteraksi karena langsung ditarik kembali ke kiosk.

### 2. **Emergency Exit dengan Password Lokal**
**Masalah**: Client hanya bisa keluar dengan Ctrl+Alt+Q jika terhubung ke server untuk verifikasi password kepala lab.

**Solusi yang Diterapkan**:
- **Password Emergency Lokal**: `labkom123`
- **Fallback Logic**: 
  1. Cek password lokal terlebih dahulu
  2. Jika cocok → langsung keluar tanpa koneksi server
  3. Jika tidak cocok → coba verifikasi ke server
  4. Jika server tidak terjangkau → tampilkan pesan error dengan hint password emergency

**File yang Diubah**: `client/src/AdminExitDialog.jsx`

```javascript
const LOCAL_EMERGENCY_PASSWORD = 'labkom123';

// Cek password lokal terlebih dahulu
if (password === LOCAL_EMERGENCY_PASSWORD) {
  await doExit();
  return;
}
```

**Pesan Error Baru**:
Jika server tidak terjangkau, user akan mendapat pesan:
```
"Server tidak dapat dijangkau. Gunakan password emergency "labkom123" untuk keluar."
```

## File yang Dimodifikasi

### 1. `client/electron/main.js`
**Fungsi yang Diubah**:
- `keepWindowVisible()` - Menambahkan force focus untuk Windows
- `scheduleFocusRecovery()` - Digunakan dengan delay dinamis
- `isKioskLocked()` - Function helper untuk deteksi mode lock
- Event handlers di `createWindow()`:
  - `blur` - Recovery dinamis berdasarkan mode
  - `minimize` - Recovery 5ms
  - `hide` - Recovery 5ms
  - `restore` - Recovery 10ms
  - `focus` - Enforce kiosk mode
  - `will-resize` - Conditional prevention
  - `move` - Conditional prevention
- `app.on('browser-window-blur')` - Recovery dinamis berdasarkan mode

### 2. `client/src/AdminExitDialog.jsx`
**Fungsi yang Diubah**:
- `handleSubmit()` - Menambahkan logika password lokal

## Cara Penggunaan

### Keluar dari Mode Client (Emergency Exit)

**Metode 1: Shortcut Keyboard**
1. Tekan `Ctrl + Alt + Q` secara bersamaan
2. Dialog akan muncul
3. Ketik password:
   - Password kepala lab dari server, ATAU
   - Password emergency lokal: `labkom123`
4. Tekan Enter atau klik "Keluar Aplikasi"

**Metode 2: Klik Pojok (5x)**
1. Di layar login, klik pojok kiri bawah 5 kali cepat
2. Dialog akan muncul
3. Lanjutkan seperti Metode 1

### Kapan Menggunakan Password Emergency?
- Server admin tidak berjalan
- Koneksi jaringan bermasalah
- Situasi darurat yang memerlukan akses cepat
- Password kepala lab lupa

## Testing yang Disarankan

### Test 1: Alt+Tab di Mode Lock
1. Buka aplikasi client
2. Di layar login (mode kiosk fullscreen)
3. Tekan Alt+Tab
4. **Expected**: Window lain mungkin muncul sekilas (<10ms) tapi langsung kembali ke kiosk
5. User tidak bisa berinteraksi dengan window lain

### Test 2: Alt+Tab di Mode Widget
1. Login sebagai siswa
2. Mode widget aktif (pojok kanan bawah)
3. Tekan Alt+Tab
4. **Expected**: Bisa berpindah window seperti biasa (recovery lebih lembut)

### Test 3: Emergency Exit - Password Lokal
1. Pastikan server admin TIDAK berjalan
2. Tekan Ctrl+Alt+Q
3. Ketik `labkom123`
4. **Expected**: Aplikasi langsung keluar tanpa error

### Test 4: Emergency Exit - Password Server
1. Pastikan server admin BERJALAN
2. Tekan Ctrl+Alt+Q
3. Ketik password kepala lab dari database
4. **Expected**: Verifikasi ke server → aplikasi keluar

### Test 5: Emergency Exit - Password Salah
1. Tekan Ctrl+Alt+Q
2. Ketik password yang salah (bukan lokal, bukan server)
3. **Expected**: 
   - Jika server online: "Password salah"
   - Jika server offline: "Server tidak dapat dijangkau. Gunakan password emergency 'labkom123' untuk keluar."

## Keamanan

### Password Emergency
⚠️ **PENTING**: Password `labkom123` adalah hardcoded di client-side JavaScript.

**Implikasi**:
- Siapapun yang bisa inspect source code bisa menemukan password ini
- Password ini HANYA untuk situasi emergency
- Untuk keamanan normal, gunakan password kepala lab dari server

**Rekomendasi**:
- Ganti password ini secara berkala di production
- Jangan share password ini ke siswa
- Monitor penggunaan emergency exit melalui log

## Build & Deploy

Setelah perubahan ini, untuk menerapkan ke production:

```bash
# Masuk ke folder client
cd client

# Build aplikasi
npm run build

# Hasil executable ada di: client/dist-electron/
```

File yang dihasilkan:
- `LabKom Siswa Setup 1.0.0.exe` - Installer untuk deploy ke PC lab

## Changelog

### Version 1.0.1 (1 April 2026)
- ✅ Perbaikan Alt+Tab bypass dengan aggressive recovery (10ms di mode lock)
- ✅ Tambah password emergency lokal `labkom123` untuk exit tanpa server
- ✅ Perbaikan conditional focus recovery berdasarkan mode (lock vs widget)
- ✅ Tambah force focus mechanism untuk Windows
- ✅ Perbaikan error message untuk memberikan hint password emergency

---

## Kontak

Jika ada pertanyaan atau issues:
- Check log di: `%APPDATA%/labkom-siswa/logs/`
- Review kode di: `client/electron/main.js` dan `client/src/AdminExitDialog.jsx`
