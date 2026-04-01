# 🚀 Quick Start - Firebase Setup untuk Labkom

## ⚡ 5 Menit Setup!

### Step 1: Buat Firebase Project
1. Buka: https://console.firebase.google.com
2. Click "Add project" → Nama: `labkom-51250`
3. Disable Analytics → Create

### Step 2: Enable Firestore
1. Sidebar → "Firestore Database" → "Create database"
2. Production mode → Location: **asia-southeast2 (Jakarta)**
3. Enable → tunggu 1-2 menit

### Step 3: Set Security Rules
1. Tab "Rules" → Copy paste:
```javascript
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {
    match /{document=**} {
      allow read, write: if true;
    }
  }
}
```
2. Publish

### Step 4: Download Service Account Key
1. ⚙️ Settings → "Service accounts"
2. "Generate new private key" → Download
3. Rename → `firebase-service-account.json`
4. Move ke → `c:\Labkom\server\firebase-service-account.json`

### Step 5: Restart Server
```bash
cd c:\Labkom\server
npm start
```

### ✅ Verify
Lihat console log:
```
[FIREBASE] ✅ Inisialisasi berhasil dengan service account key
```

## 🎯 Test API
```bash
# Test endpoint
curl http://localhost:3001/api/students

# Harus dapat response (bukan error 503)
```

## ⚠️ IMPORTANT
**JANGAN COMMIT** `firebase-service-account.json` ke git!  
File sudah otomatis di-ignore di `.gitignore`

## 📚 Detail Lengkap
Lihat: `FIREBASE-SETUP-GUIDE.md`

---

**Ready!** Firebase sekarang aktif dan data akan tersimpan permanen di cloud! ☁️
