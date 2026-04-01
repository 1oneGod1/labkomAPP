# 🔥 Firestore Database Structure

## Overview
Aplikasi Labkom menggunakan **hybrid architecture**:
- **Firebase Firestore** → Data persistent (students, sessions, checks, settings, history)
- **LAN Server (Socket.IO)** → Real-time monitoring, commands, screenshots (tidak di-store ke database)

---

## Collections Structure

### 1️⃣ **students** (Koleksi Mahasiswa)
```
students/{studentId}
├── id: string (NIM)
├── nim: string
├── name: string
├── class: string
├── created_at: timestamp
└── updated_at: timestamp
```

**Indexes:**
- `nim` (ascending)
- `class` (ascending)

---

### 2️⃣ **lab_computers** (Master Data Komputer Lab)
```
lab_computers/{computerId}
├── id: string (auto-generated)
├── name: string (e.g., "LAB-PC-01")
├── location: string (optional, e.g., "Row 1, Seat 3")
├── status: string ("active" | "inactive" | "maintenance")
├── created_at: timestamp
└── updated_at: timestamp
```

**Indexes:**
- `name` (ascending)
- `status` (ascending)

---

### 3️⃣ **sessions** (Session Mahasiswa di Komputer)
```
sessions/{sessionId}
├── id: string (auto-generated)
├── student_id: string (NIM, reference to students)
├── computer_id: string (reference to lab_computers)
├── computer_name: string (denormalized untuk performa)
├── login_time: timestamp
├── logout_time: timestamp | null
├── duration_minutes: number | null
├── status: string ("active" | "completed")
├── created_at: timestamp
└── updated_at: timestamp
```

**Indexes:**
- `student_id` (ascending)
- `status` (ascending)
- `login_time` (descending)
- Compound: `status` (ascending) + `login_time` (descending)

---

### 4️⃣ **facility_checks** (Pengecekan Fasilitas Lab)
```
facility_checks/{checkId}
├── id: string (auto-generated)
├── session_id: string (reference to sessions)
├── student_id: string (NIM)
├── computer_id: string
├── computer_name: string (denormalized)
├── mouse: boolean
├── keyboard: boolean
├── monitor: boolean
├── headset: boolean
├── checked_at: timestamp
├── created_at: timestamp
└── updated_at: timestamp
```

**Indexes:**
- `session_id` (ascending)
- `student_id` (ascending)
- `checked_at` (descending)

---

### 5️⃣ **control_settings** (Pengaturan Kontrol Global)
```
control_settings/global
├── id: "global" (singleton document)
├── lock_enabled: boolean
├── client_locked: boolean (untuk backward compatibility)
├── updated_at: timestamp
└── updated_by: string (admin identifier)
```

**Note:** Ini adalah singleton document dengan ID tetap "global"

---

### 6️⃣ **usage_history** (History Penggunaan - Optional, untuk analytics)
```
usage_history/{historyId}
├── id: string (auto-generated)
├── date: string (format: "YYYY-MM-DD")
├── total_sessions: number
├── total_students: number
├── average_duration: number (minutes)
├── peak_hour: string (e.g., "14:00")
├── created_at: timestamp
└── updated_at: timestamp
```

**Indexes:**
- `date` (descending)

---

## Data Flow

### 📥 **Data yang DISIMPAN ke Firebase:**
1. Master data mahasiswa (CRUD students)
2. Master data komputer lab (CRUD lab_computers)
3. Session login/logout mahasiswa
4. Facility checks saat login
5. Control settings (lock/unlock)
6. Usage history untuk laporan

### 🔄 **Data yang TIDAK DISIMPAN (Real-time via LAN):**
1. Live PC status (online/offline)
2. Screenshots real-time
3. Active applications monitoring
4. Commands (shutdown, sleep, restart, lock)
5. Network discovery (UDP broadcast)

---

## Migration dari MySQL

### Mapping Tables → Collections

| MySQL Table | Firestore Collection | Notes |
|-------------|---------------------|-------|
| `students` | `students` | Direct migration |
| `lab_computers` | `lab_computers` | Direct migration |
| `sessions` | `sessions` | Direct migration |
| `facility_checks` | `facility_checks` | Direct migration |
| N/A | `control_settings` | Singleton, previously in-memory |
| N/A | `usage_history` | New, untuk analytics |

### Auto-increment ID → Firestore Document ID
- MySQL menggunakan auto-increment integer
- Firestore menggunakan auto-generated string ID
- NIM tetap digunakan sebagai unique identifier untuk students

---

## Security Rules (untuk production)

```javascript
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {
    
    // Students collection
    match /students/{studentId} {
      allow read: if true;  // Public read
      allow write: if request.auth != null;  // Authenticated write
    }
    
    // Lab computers
    match /lab_computers/{computerId} {
      allow read: if true;
      allow write: if request.auth != null;
    }
    
    // Sessions
    match /sessions/{sessionId} {
      allow read: if true;
      allow create: if true;  // Students can create sessions
      allow update, delete: if request.auth != null;
    }
    
    // Facility checks
    match /facility_checks/{checkId} {
      allow read: if true;
      allow create: if true;
      allow update, delete: if request.auth != null;
    }
    
    // Control settings (admin only)
    match /control_settings/{settingId} {
      allow read: if true;
      allow write: if request.auth != null;
    }
    
    // Usage history
    match /usage_history/{historyId} {
      allow read: if true;
      allow write: if request.auth != null;
    }
  }
}
```

---

## Performance Optimization

### Indexing Strategy
- Index pada field yang sering di-query
- Compound index untuk query kompleks
- Hindari index pada field yang jarang digunakan

### Denormalization
- `computer_name` di-denormalize ke `sessions` dan `facility_checks`
- Mengurangi read operations
- Trade-off: harus update di multiple places saat computer name berubah

### Batching
- Gunakan batch write untuk multiple operations
- Maksimal 500 operations per batch
- Atomic transactions untuk consistency

---

## Backup Strategy

### Automated Backups
1. Firebase Console → Project Settings → Backups
2. Schedule daily backups
3. Retention policy: 30 days

### Manual Export
```bash
gcloud firestore export gs://[BUCKET_NAME]
```

### Migration Script
```javascript
// Export data dari Firestore ke JSON
// Untuk backup atau migration purposes
```

---

## Cost Estimation

### Firestore Pricing (Free Tier)
- **Reads:** 50,000/day
- **Writes:** 20,000/day
- **Deletes:** 20,000/day
- **Storage:** 1 GB

### Estimated Usage (untuk 40 PC lab)
- **Daily Reads:** ~5,000 (well within limit)
- **Daily Writes:** ~500 (well within limit)
- **Storage:** ~50 MB (well within limit)

**Conclusion:** Free tier sudah cukup untuk lab scale! 🎉

---

## Setup Instructions

### 1. Create Firebase Project
1. Go to https://console.firebase.google.com
2. Create new project: "labkom-app"
3. Enable Firestore Database

### 2. Generate Service Account Key
1. Project Settings → Service Accounts
2. Generate new private key
3. Download JSON file
4. Save as `server/firebase-service-account.json`

### 3. Update .env
```env
# Firebase Configuration
FIREBASE_SERVICE_ACCOUNT_KEY=./firebase-service-account.json
FIREBASE_DATABASE_URL=https://labkom-app-default-rtdb.firebaseio.com
```

### 4. Run Migration (when ready)
```bash
cd server
node scripts/migrate-mysql-to-firebase.js
```

---

## Maintenance

### Regular Tasks
- [ ] Monitor Firestore usage (Firebase Console)
- [ ] Review and optimize queries
- [ ] Clean old sessions (auto-cleanup function)
- [ ] Export backups monthly

### Monitoring
- Setup Cloud Functions for automated cleanup
- Alert pada usage anomaly
- Track query performance

---

**Last Updated:** April 1, 2026
**Maintained By:** Labkom Team
