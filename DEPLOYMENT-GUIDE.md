# 🚀 Deployment Guide - LabKom Management System

## 📋 Pre-Deployment Checklist

### ✅ Database Configuration
- [x] Firebase Firestore sudah dikonfigurasi
- [ ] Download Firebase Service Account Key dari Firebase Console
- [ ] Simpan sebagai `server/firebase-service-account.json`
- [ ] Update `server/.env` dengan path yang benar

### ✅ System Requirements

**Server PC:**
- Windows 10/11
- Node.js v18+ 
- RAM minimum 4GB
- Port 3001 terbuka di firewall

**Client PC:**
- Windows 10/11
- Jaringan LAN yang sama dengan server
- Resolusi minimum 1366x768

---

## 🔧 Build Instructions

### 1. Build Server (Backend)
```bash
cd server
npm install
node src/index.js
```

**Note:** Server akan berjalan di port 3001. Pastikan tidak ada aplikasi lain yang menggunakan port ini.

### 2. Build Admin Dashboard (Electron App)
```bash
cd admin
npm install
npm run build

# Output: admin/release/LabKom Admin Setup.exe
```

**Build Configuration:**
- File: `admin/package.json`
- Auto-updater enabled
- Target: Windows x64 NSIS installer

### 3. Build Client (Electron App)
```bash
cd client
npm install
npm run build

# Output: client/release/LabKom Client Setup.exe
```

**Build Configuration:**
- File: `client/package.json`
- Kiosk mode enabled
- Target: Windows x64 NSIS installer

---

## 📦 Installation Steps

### Step 1: Setup Server PC

1. **Install Node.js**
   - Download dari https://nodejs.org/
   - Versi LTS (v18 atau lebih baru)

2. **Configure Firebase**
   ```bash
   # Download service account key dari Firebase Console
   # Project Settings > Service Accounts > Generate new private key
   # Simpan ke: server/firebase-service-account.json
   ```

3. **Configure Environment**
   ```bash
   cd server
   # Edit .env file
   FIREBASE_SERVICE_ACCOUNT_KEY=./firebase-service-account.json
   FIREBASE_PROJECT_ID=labkom-51250
   ADMIN_PASSWORD=kepalalab123  # Ganti dengan password yang aman!
   ```

4. **Start Server**
   ```bash
   npm install
   node src/index.js
   ```

5. **Server akan menampilkan:**
   ```
   [SERVER] 🚀 LabKom Server running on http://192.168.x.x:3001
   [FIREBASE] ✅ Inisialisasi berhasil
   ```

### Step 2: Install Admin Dashboard

1. **Di PC Server atau PC Admin:**
   - Jalankan `LabKom Admin Setup.exe`
   - Follow installer wizard
   - Launch aplikasi

2. **Login:**
   - Password: `kepalalab123` (atau sesuai .env)

3. **Cek Status Server:**
   - Buka tab "Status Server"
   - Pastikan status ONLINE
   - Copy IP address untuk client setup

### Step 3: Install Client di Lab PC

1. **Install di setiap PC Lab:**
   - Jalankan `LabKom Client Setup.exe`
   - Follow installer wizard

2. **Configure Auto-Start:**
   ```
   Aplikasi otomatis ditambahkan ke Windows Startup
   Client akan auto-start saat PC dinyalakan
   ```

3. **Konfigurasi Server Connection:**
   - Saat pertama kali run, client akan scan network (UDP broadcast)
   - Atau masukkan manual IP server: `http://192.168.x.x:3001`
   - Client akan connect otomatis

---

## 🔒 Security Setup

### 1. Firewall Configuration

**Windows Firewall - Server PC:**
```
Inbound Rules:
- Allow TCP Port 3001 (LabKom Server)
- Allow UDP Port 41234 (Discovery Broadcast)
```

**Command:**
```powershell
# Run as Administrator
netsh advfirewall firewall add rule name="LabKom Server" dir=in action=allow protocol=TCP localport=3001
netsh advfirewall firewall add rule name="LabKom Discovery" dir=in action=allow protocol=UDP localport=41234
```

### 2. Change Default Passwords

**Admin Password (server/.env):**
```env
ADMIN_PASSWORD=GANTI_DENGAN_PASSWORD_KUAT
```

**Emergency Exit Password (client/src/AdminExitDialog.jsx):**
```javascript
const LOCAL_EMERGENCY_PASSWORD = 'GANTI_DENGAN_PASSWORD_KUAT';
```

**Rebuild apps setelah mengganti password!**

### 3. Firebase Security Rules

```javascript
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {
    match /{document=**} {
      allow read, write: if request.auth != null;
    }
  }
}
```

---

## 🛠️ Configuration

### Server Configuration (`server/.env`)

```env
# Server
PORT=3001
NODE_ENV=production

# Firebase
FIREBASE_SERVICE_ACCOUNT_KEY=./firebase-service-account.json
FIREBASE_PROJECT_ID=labkom-51250

# Admin
ADMIN_PASSWORD=kepalalab123

# Database (Optional - Legacy fallback)
DB_HOST=localhost
DB_PORT=3306
DB_USER=root
DB_PASSWORD=
DB_NAME=labkom_db
```

### Client Configuration

**Auto-Discovery:**
- Client otomatis scan network menggunakan UDP broadcast
- Server mengirimkan beacon setiap 3 detik
- Client connect ke server yang pertama ditemukan

**Manual Configuration:**
- Jika auto-discovery gagal
- Klik "Masukkan Manual"
- Input: `http://[IP_SERVER]:3001`

---

## 🧪 Testing

### 1. Test Server Connection

**From Admin Dashboard:**
```
1. Buka tab "Status Server"
2. Klik "Ping" untuk setiap IP
3. Pastikan status: "Dapat dijangkau - LabKom API OK"
```

**From Browser:**
```
http://[IP_SERVER]:3001/health

Response:
{
  "status": "ok",
  "timestamp": "2026-04-02T10:00:00.000Z"
}
```

### 2. Test Client Connection

**Method 1: Auto-Discovery**
```
1. Start client app
2. Tunggu "Mencari server..."
3. Server ditemukan → Show server info
4. Klik "Hubungkan"
```

**Method 2: Manual Input**
```
1. Klik "Masukkan manual"
2. Input: http://192.168.1.100:3001
3. Klik "Hubungkan"
```

### 3. Test Student Login

```
1. Client terhubung → Lock screen muncul
2. Input NIS dan Password
3. Check facility → Submit
4. Dashboard admin: PC status berubah "Digunakan"
5. Activity monitoring: Real-time browser & app tracking
```

---

## 🐛 Troubleshooting

### Server tidak start
```bash
# Check port sudah digunakan?
netstat -ano | findstr :3001

# Kill process yang menggunakan port
taskkill /PID [PID_NUMBER] /F

# Restart server
node src/index.js
```

### Client tidak menemukan server
```
1. Pastikan Server dan Client di network yang sama
2. Check Windows Firewall allow port 3001
3. Try manual input IP server
4. Check server logs untuk connection attempts
```

### Firebase Connection Error
```
Error: Firebase service account key tidak ditemukan

Solution:
1. Download key dari Firebase Console
2. Simpan ke server/firebase-service-account.json
3. Restart server
```

### Alt+Tab masih bisa bypass lock screen
```
Note: Ini limitasi Windows OS security
- Alt+Tab bypass sudah diminimalisir dengan aggressive focus loop
- Window akan kembali dalam <0.2 detik
- Emergency exit: Ctrl+Alt+Q → password "labkom123"
```

---

## 📊 Monitoring & Maintenance

### Server Logs

**Location:**
```
server/server.log (rotating daily)
```

**Monitor:**
```bash
# Real-time logs
tail -f server/server.log

# Error logs
grep "ERROR" server/server.log
```

### Client Logs

**Location:**
```
C:\Users\[USERNAME]\AppData\Roaming\labkom-client\logs\
```

### Database Backup

**Firestore Auto-Backup:**
```
1. Firebase Console → Project Settings → Backups
2. Enable daily automatic backups
3. Retention: 30 days
```

**Manual Export:**
```bash
gcloud firestore export gs://[BUCKET_NAME]/[EXPORT_PATH]
```

---

## 🔄 Updates

### Auto-Update System

**Admin App:**
```
1. Check for updates: Tab "Status Server" → "Periksa Pembaruan"
2. Jika ada update: Banner muncul otomatis
3. Download → Install & Restart
```

**Client App:**
```
- Auto-check saat startup
- Silent install di background
- Auto-restart setelah update
```

### Manual Update

**Server:**
```bash
cd server
git pull origin main
npm install
node src/index.js
```

**Apps:**
```bash
# Build ulang
cd admin && npm run build
cd ../client && npm run build

# Deploy installer ke client PC
```

---

## 📈 Performance Optimization

### Server Performance

**Recommended Specs:**
- CPU: Intel i5 / Ryzen 5 atau lebih
- RAM: 8GB minimum, 16GB recommended
- SSD for faster database access

**Optimization:**
```javascript
// server/src/index.js
// Adjust concurrent connections limit
const maxClients = 50; // Default untuk 40 PC + 10 admin

// Socket.IO optimization
io.set('transports', ['websocket', 'polling']);
io.set('heartbeatInterval', 10000);
```

### Client Performance

**Recommended Specs per PC:**
- CPU: Intel Core i3 / Ryzen 3 atau lebih
- RAM: 4GB minimum
- Screen: 1366x768 minimum

---

## 📞 Support

### Common Issues

| Issue | Solution |
|-------|----------|
| Server tidak start | Check port 3001, restart server |
| Client tidak connect | Check firewall, manual input IP |
| Login gagal | Check NIS di database |
| Screenshot tidak muncul | Restart client app |
| Activity tracking tidak jalan | Check Socket.IO connection |

### Contact

**Developer:**
- Email: support@labkom.edu
- Docs: https://docs.labkom.edu
- GitHub: https://github.com/labkom/management-system

---

## ✅ Post-Deployment Verification

- [ ] Server running dan accessible dari LAN
- [ ] Firebase connection success
- [ ] Admin dashboard bisa login
- [ ] Minimal 1 client berhasil connect
- [ ] Student bisa login dengan NIS
- [ ] Activity monitoring real-time working
- [ ] Screenshot sharing working
- [ ] Attention mode tested
- [ ] Emergency exit (Ctrl+Alt+Q) working
- [ ] Firewall configured
- [ ] Default passwords diganti
- [ ] Auto-update tested

---

**Last Updated:** April 2, 2026  
**Version:** 2.0.0  
**Maintained By:** LabKom Development Team
