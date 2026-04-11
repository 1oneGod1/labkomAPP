# 🚀 Panduan Build & Release LabKom

## Struktur Aplikasi

| Komponen | Folder | Deskripsi |
|----------|--------|-----------|
| **Admin** | `admin/` | Dashboard kepala lab (Electron + React + bundled server) |
| **Client** | `client/` | Aplikasi kiosk siswa (Electron + React) |
| **Server** | `server/` | Express.js API + Socket.IO (di-bundle dalam Admin) |

---

## 🔨 Build Lokal (Manual)

### Prasyarat
- Node.js 20+
- npm

### Build Admin (termasuk server)
```bash
cd server
npm install

cd ../admin
npm install
npm run electron:build
```
Hasil: `admin/dist-electron/LabKom Admin - Dashboard Setup *.exe`

### Build Client
```bash
cd client
npm install
npm run electron:build
```
Hasil: `client/dist-electron/LabKom Siswa Setup *.exe`

---

## 🤖 Auto-Release via GitHub Actions

GitHub Actions workflow sudah dikonfigurasi untuk build & publish otomatis ke GitHub Releases.

### Release Admin App
```bash
# 1. Naikkan versi di admin/package.json
# 2. Commit perubahan
git add -A && git commit -m "release: admin v1.0.1"

# 3. Buat tag dan push
git tag admin-v1.0.1
git push origin main --tags
```

### Release Client App
```bash
# 1. Naikkan versi di client/package.json
# 2. Commit perubahan
git add -A && git commit -m "release: client v1.0.1"

# 3. Buat tag dan push
git tag client-v1.0.1
git push origin main --tags
```

### Release Keduanya Sekaligus
```bash
git add -A && git commit -m "release: v1.0.1"
git tag admin-v1.0.1
git tag client-v1.0.1
git push origin main --tags
```

---

## 🔄 Auto-Update

### Client (Siswa)
- **Auto-download**: Update didownload otomatis di background
- **Auto-install**: Terinstall otomatis saat app ditutup/restart
- Siswa tidak perlu melakukan apapun - update terjadi transparan

### Admin (Kepala Lab)
- **Manual check**: Klik tombol "Cek Update" di dashboard
- **Download**: Konfirmasi download setelah update ditemukan
- **Install**: Pilih "Install Sekarang" atau otomatis saat app ditutup

### Publish Config
Kedua app menggunakan GitHub Releases dari repo `1oneGod1/labkomAPP`:
- Admin: `admin/package.json` → `build.publish`
- Client: `client/package.json` → `build.publish`

---

## 📋 Checklist Sebelum Release

- [ ] Test semua fitur di mode development
- [ ] Naikkan `version` di `package.json` yang relevan
- [ ] Pastikan `server/.env` TIDAK ikut ter-bundle (sudah di-exclude via filter)
- [ ] Pastikan `firebase-service-account.json` ada di server jika menggunakan Firebase
- [ ] Commit semua perubahan
- [ ] Buat tag dengan format yang benar (`admin-v*` atau `client-v*`)
- [ ] Push tag ke GitHub

---

## 🛠 Troubleshooting

### Build gagal di GitHub Actions
- Pastikan `secrets.GITHUB_TOKEN` sudah tersedia (otomatis dari GitHub)
- Cek apakah `npm ci` berhasil (perlu `package-lock.json`)

### Auto-update tidak bekerja
- Pastikan GitHub Release berstatus **Published** (bukan Draft)
- File `latest.yml` harus ada di release assets
- Cek log di `%APPDATA%/labkom-client/logs/` atau `%APPDATA%/labkom-admin/logs/`

### Client tidak menemukan server
- Admin app harus berjalan terlebih dahulu (server otomatis start)
- Pastikan kedua PC dalam jaringan LAN yang sama
- UDP broadcast port 41234 harus tidak diblokir firewall
