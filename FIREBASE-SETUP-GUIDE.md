# 🔥 Firebase Setup Guide - Labkom App

## 📋 Overview

Aplikasi Labkom sekarang menggunakan **hybrid architecture**:
- **Firebase Firestore** → Database persistent untuk students, sessions, facility checks, dll
- **LAN Server** → Real-time monitoring via Socket.IO (screenshots, commands, PC status)

**PENTING:** Aplikasi masih bisa berjalan tanpa Firebase (mode LAN-only), tetapi data tidak akan tersimpan secara permanen.

---

## 🎯 Step-by-Step Setup

### 1️⃣ Create Firebase Project

1. **Buka Firebase Console**
   - Go to: https://console.firebase.google.com
   - Sign in dengan Google Account

2. **Create New Project**
   - Click "Add project"
   - Project name: `labkom-51250` (atau nama lain)
   - Disable Google Analytics (optional, tidak diperlukan untuk lab)
   - Click "Create project"

3. **Tunggu proses creation** (~30 detik)

---

### 2️⃣ Enable Firestore Database

1. **Navigate to Firestore**
   - Di sidebar kiri, click "Firestore Database"
   - Click "Create database"

2. **Choose Production Mode**
   - Start in **production mode**
   - Click "Next"

3. **Select Location**
   - Pilih location terdekat: **asia-southeast2 (Jakarta)** untuk performance terbaik
   - Click "Enable"

4. **Tunggu Firestore initialization** (~1-2 menit)

---

### 3️⃣ Configure Firestore Security Rules

1. **Go to Rules Tab**
   - Di Firestore Database, click tab "Rules"

2. **Copy-paste rules berikut:**

```javascript
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {
    
    // Public read, authenticated write for all collections
    // Untuk lab environment, kita allow semua akses
    match /{document=**} {
      allow read: if true;
      allow write: if true;
    }
  }
}
```

3. **Publish rules**
   - Click "Publish"

**Note:** Rules ini sangat permissive untuk kemudahan development. Untuk production, gunakan rules yang lebih strict dari `FIRESTORE-STRUCTURE.md`.

---

### 4️⃣ Generate Service Account Key

1. **Go to Project Settings**
   - Click gear icon ⚙️ di sidebar
   - Select "Project settings"

2. **Navigate to Service Accounts**
   - Click tab "Service accounts"

3. **Generate Private Key**
   - Click "Generate new private key"
   - Click "Generate key" pada dialog konfirmasi
   - File JSON akan terdownload ke komputer Anda

4. **Rename & Move File**
   - Rename file yang didownload menjadi: `firebase-service-account.json`
   - Move file ke folder: `c:\Labkom\server\`
   - Path final: `c:\Labkom\server\firebase-service-account.json`

---

### 5️⃣ Update Server .env File

1. **Buka file:** `c:\Labkom\server\.env`

2. **Tambahkan Firebase configuration:**

```env
# ════════════════════════════════════════════════════════════════
# FIREBASE CONFIGURATION
# ════════════════════════════════════════════════════════════════

# Path ke service account key file (relative to server folder)
FIREBASE_SERVICE_ACCOUNT_KEY=./firebase-service-account.json

# Project ID dari Firebase Console
FIREBASE_PROJECT_ID=labkom-51250
```

3. **Save file**

---

### 6️⃣ Verify Installation

1. **Restart Server**
   - Stop server jika sedang running (Ctrl+C)
   - Jalankan kembali: `npm start` atau `npm run dev`

2. **Check Console Output**
   - Anda harus melihat: `[FIREBASE] ✅ Inisialisasi berhasil dengan service account key`
   - Jika melihat error, cek langkah-langkah sebelumnya

3. **Test API Endpoint**
   - Buka browser: `http://localhost:3001/api/students`
   - Anda harus mendapat response (empty array jika belum ada data)

---

## 🔒 Security Best Practices

### ⚠️ JANGAN COMMIT SERVICE ACCOUNT KEY

File `firebase-service-account.json` berisi credentials sensitif!

1. **Pastikan file ada di .gitignore:**

```gitignore
# Firebase
firebase-service-account.json
```

2. **Verify .gitignore:**
   ```bash
   cd c:\Labkom\server
   git status
   ```
   - `firebase-service-account.json` TIDAK boleh muncul di staged files

---

## 📊 Firebase Console - Useful Links

Setelah setup, bookmark links ini untuk monitoring:

### Firestore Database
```
https://console.firebase.google.com/project/labkom-51250/firestore
```

### Project Settings
```
https://console.firebase.google.com/project/labkom-51250/settings/general
```

### Usage & Billing
```
https://console.firebase.google.com/project/labkom-51250/usage
```

---

## 🎨 Firestore Collections Structure

Setelah setup, collections ini akan otomatis dibuat saat data pertama kali ditulis:

```
labkom-51250 (Firestore Database)
├── students/              # Data mahasiswa
├── lab_computers/         # Master data komputer lab
├── sessions/              # Session login/logout mahasiswa
├── facility_checks/       # Pengecekan fasilitas
└── control_settings/      # Global settings (singleton)
```

Lihat detail structure di: `FIRESTORE-STRUCTURE.md`

---

## 🧪 Testing Firebase Integration

### Test 1: Create Student

```bash
curl -X POST http://localhost:3001/api/students \
  -H "Content-Type: application/json" \
  -d '{
    "nis": "12345",
    "nama_lengkap": "Test Student",
    "kelas": "12 RPL 1",
    "password": "test123"
  }'
```

**Expected Response:**
```json
{
  "success": true,
  "message": "Siswa berhasil ditambahkan.",
  "data": {
    "id": "auto-generated-id",
    "nis": "12345",
    "nama_lengkap": "Test Student",
    "kelas": "12 RPL 1",
    "is_active": 1
  }
}
```

### Test 2: Get All Students

```bash
curl http://localhost:3001/api/students
```

### Test 3: Verify in Firebase Console

1. Buka: https://console.firebase.google.com/project/labkom-51250/firestore
2. Anda harus melihat collection `students` dengan document yang baru dibuat
3. Click document untuk melihat fields: `nis`, `nama_lengkap`, `kelas`, `password_hash`, dll

---

## 🚨 Troubleshooting

### Error: "Firestore not available"

**Symptoms:**
```json
{
  "success": false,
  "message": "Database tidak tersedia. Silakan setup Firebase terlebih dahulu."
}
```

**Solutions:**
1. Check `firebase-service-account.json` ada di `server/` folder
2. Check `.env` file memiliki `FIREBASE_SERVICE_ACCOUNT_KEY=./firebase-service-account.json`
3. Restart server
4. Check console logs untuk error messages

---

### Error: "Permission Denied"

**Symptoms:**
```
FirebaseError: Missing or insufficient permissions
```

**Solutions:**
1. Update Firestore Rules (lihat Step 3)
2. Pastikan rules allow read/write
3. Publish rules dan tunggu ~1 minute

---

### Error: "Project not found"

**Symptoms:**
```
Error: Project labkom-51250 not found
```

**Solutions:**
1. Verify project ID di Firebase Console
2. Update `FIREBASE_PROJECT_ID` di `.env`
3. Restart server

---

### Warning: Service Account Key tidak ditemukan

**Symptoms:**
```
[FIREBASE] ⚠️  Service account key tidak ditemukan
[FIREBASE] Aplikasi akan berjalan tanpa database persistence (hanya LAN server)
```

**Solutions:**
1. Download service account key dari Firebase Console
2. Rename menjadi `firebase-service-account.json`
3. Move ke `c:\Labkom\server\` folder
4. Restart server

---

## 📈 Monitoring & Maintenance

### Check Firestore Usage

1. Go to: https://console.firebase.google.com/project/labkom-51250/usage
2. Monitor:
   - **Reads:** Should be < 50,000/day (free tier)
   - **Writes:** Should be < 20,000/day (free tier)
   - **Storage:** Should be < 1 GB (free tier)

### Free Tier Limits

Firebase Spark Plan (Free):
- ✅ **50,000 reads/day** → Cukup untuk ~150 students × 300 reads each
- ✅ **20,000 writes/day** → Cukup untuk semua sessions + checks
- ✅ **1 GB storage** → Cukup untuk data tahunan
- ✅ **10 GB bandwidth/month** → Cukup untuk LAN usage

**Conclusion:** Free tier sangat cukup untuk lab scale! 🎉

---

## 🔄 Migration dari MySQL

Jika Anda sudah punya data di MySQL dan ingin migrate ke Firebase:

### Option 1: Manual Migration via Admin Panel

1. Export data dari MySQL
2. Import satu per satu via Admin Panel UI
3. Verify data di Firebase Console

### Option 2: Automated Migration Script

*Coming soon - script untuk migrate bulk data dari MySQL ke Firestore*

---

## ✅ Verification Checklist

Setelah setup, pastikan:

- [ ] Firebase project created
- [ ] Firestore Database enabled
- [ ] Security rules published
- [ ] Service account key downloaded
- [ ] `firebase-service-account.json` di folder `server/`
- [ ] `.env` updated dengan Firebase config
- [ ] `firebase-service-account.json` ada di `.gitignore`
- [ ] Server restart dan log menunjukkan "✅ Inisialisasi berhasil"
- [ ] Test API `/api/students` berfungsi
- [ ] Data muncul di Firestore Console

---

## 📞 Need Help?

Jika mengalami kesulitan setup:

1. **Check console logs** untuk error messages detail
2. **Check Firebase Console** untuk status project & database
3. **Verify all steps** di guide ini sudah diikuti
4. **Check file permissions** untuk `firebase-service-account.json`

---

**Setup Guide Version:** 1.0  
**Last Updated:** April 1, 2026  
**Maintained By:** Labkom Team

---

## 🎓 Next Steps

Setelah Firebase setup selesai:

1. ✅ **Setup Admin Panel** → Buka admin app untuk manage students
2. ✅ **Test Client Login** → Test student login dengan credentials
3. ✅ **Monitor Real-time** → Check PC monitoring via LAN server
4. ✅ **Review Data** → Check Firestore Console untuk verify data persistence

**Selamat!** Aplikasi Labkom Anda sekarang menggunakan cloud database! ☁️🎉
