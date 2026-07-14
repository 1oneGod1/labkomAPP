// ─── Firebase Admin SDK Configuration ───────────────────────────────────────
// Untuk akses Firestore dari backend server

const admin = require('firebase-admin');

// Firebase project credentials
const firebaseConfig = {
  projectId: "labkom-51250",
  // Untuk Admin SDK, kita perlu service account key
  // Download dari Firebase Console > Project Settings > Service Accounts
};

let firebaseApp;

try {
  // Cek apakah sudah diinisialisasi
  if (admin.apps.length === 0) {
    // Development: gunakan service account key file jika ada
    if (process.env.FIREBASE_SERVICE_ACCOUNT_KEY) {
      const serviceAccount = require(process.env.FIREBASE_SERVICE_ACCOUNT_KEY);
      
      firebaseApp = admin.initializeApp({
        credential: admin.credential.cert(serviceAccount),
        projectId: firebaseConfig.projectId,
      });
      
      console.log('[FIREBASE] ✅ Inisialisasi berhasil dengan service account key');
    } else {
      console.warn('[FIREBASE] ⚠️  Service account key tidak ditemukan');
      console.log('[FIREBASE] Download service account key dari:');
      console.log('[FIREBASE] Firebase Console > Project Settings > Service Accounts');
      console.log('[FIREBASE] Simpan sebagai firebase-service-account.json dan set di .env');
      console.log('[FIREBASE] Aplikasi akan berjalan tanpa database persistence (hanya LAN server)');
    }
  } else {
    firebaseApp = admin.app();
  }
} catch (error) {
  console.error('[FIREBASE] ❌ Gagal inisialisasi:', error.message);
  console.log('[FIREBASE] Aplikasi akan berjalan tanpa database persistence (hanya LAN server)');
}

// Export Firestore instance
const db = firebaseApp ? admin.firestore() : null;

// Firestore settings untuk performance
if (db) {
  db.settings({
    ignoreUndefinedProperties: true,
    timestampsInSnapshots: true,
  });
}

module.exports = {
  admin,
  db,
  firebaseApp,
};
