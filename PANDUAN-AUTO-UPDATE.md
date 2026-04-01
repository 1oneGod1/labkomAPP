# Panduan Auto-Update LabKom

## Cara Kerja

### Admin App
- Saat startup, otomatis cek update setelah 10 detik (hanya di production).
- Muncul banner kuning di atas dashboard jika ada versi baru.
- Kepala lab klik **"Unduh Sekarang"** → progress bar tampil → klik **"Install & Restart"**.

### Client App (PC Siswa)
- Cek update setiap 30 detik setelah startup (hanya di production).
- Download secara **diam-diam di background** — tidak mengganggu sesi siswa.
- Update diinstall otomatis saat app ditutup / PC restart.

---

## Setup Awal: GitHub Releases (GRATIS)

### Langkah 1 — GitHub Repository ✅ SUDAH SIAP
Repo sudah tersedia di: **https://github.com/1oneGod1/labkom-releases**

Token sudah dimiliki — lihat Langkah 3 untuk cara pakainya.

### Langkah 2 — Config sudah diisi ✅
Semua 4 file sudah dikonfigurasi dengan:
- `owner`: `1oneGod1`
- `repo`: `labkom-releases`

Tidak perlu edit lagi.

### Langkah 3 — Set Environment Variable GH_TOKEN
Saat build, electron-builder butuh token GitHub untuk upload.

**Windows (PowerShell):**
```powershell
$env:GH_TOKEN = "ghp_xxxxxxxxxxxxxxxxxxxx"
```
Atau tambahkan ke System Environment Variables permanen.

### Langkah 4 — Build & Publish versi baru
```powershell
# Build Admin + upload ke GitHub Releases
cd C:\Labkom\admin
$env:GH_TOKEN = "TOKEN_GITHUB_ANDA"
npm run electron:build -- --publish always

# Build Client + upload ke GitHub Releases
cd C:\Labkom\client
$env:GH_TOKEN = "TOKEN_GITHUB_ANDA"
npm run electron:build -- --publish always
```

> ⚠️ **JANGAN** tulis token langsung di file. Selalu set via PowerShell seperti di atas.

---

## Cara Update ke Versi Baru

1. Edit `"version"` di `package.json` (contoh: `"1.0.0"` → `"1.1.0"`).
2. Jalankan perintah build di Langkah 4.
3. Semua PC yang sudah install versi lama akan otomatis mendapat notifikasi!

---

## Alternatif: Server Sendiri (tanpa GitHub)

Jika tidak mau pakai GitHub, bisa hosting file update di PC/server lokal:

```json
"publish": [{
  "provider": "generic",
  "url": "http://192.168.1.1/updates/admin"
}]
```

Syarat: server HTTP yang bisa diakses dari semua PC lab.  
Taruh file `latest.yml` + installer `.exe` di folder tersebut.

---

## Lokasi Log File

Jika ada masalah update, cek log di:
- Admin: `%APPDATA%\LabKom Admin\logs\main.log`
- Client: `%APPDATA%\LabKom Client\logs\main.log`
