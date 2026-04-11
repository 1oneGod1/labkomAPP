// ─── Firebase Client SDK Configuration ───────────────────────────────────────
// Digunakan HANYA sebagai fallback autentikasi ketika server LAN tidak tersedia
// Firebase Client SDK menggunakan public API key (bukan service account)
// Data tetap dilindungi oleh Firestore Security Rules

// PENTING: API key Firebase client bersifat publik dan aman untuk di-embed
// Keamanan data dijaga oleh Firestore Security Rules, bukan API key

import { initializeApp } from 'firebase/app';
import { getFirestore, collection, query, where, limit, getDocs } from 'firebase/firestore';

const firebaseConfig = {
  apiKey: "AIzaSyDummyKey-GANTI-DENGAN-API-KEY-ASLI",
  authDomain: "labkom-51250.firebaseapp.com",
  projectId: "labkom-51250",
  storageBucket: "labkom-51250.appspot.com",
  messagingSenderId: "000000000000",
  appId: "1:000000000000:web:0000000000000000000000"
};

let app = null;
let db = null;

try {
  app = initializeApp(firebaseConfig);
  db = getFirestore(app);
  console.log('[Firebase Client] ✅ Initialized');
} catch (error) {
  console.warn('[Firebase Client] ⚠️ Failed to initialize:', error.message);
}

/**
 * Login via Firebase Firestore (fallback ketika server LAN offline)
 * 
 * CATATAN: Fungsi ini membutuhkan bcrypt comparison.
 * Di Electron, kita kirim ke main process untuk compare.
 * Di browser dev, kita skip (butuh server).
 * 
 * @param {string} nis - Nomor Induk Siswa
 * @param {string} password - Password plain text
 * @param {string} pcName - Nama PC
 * @returns {Promise<{success: boolean, message: string, data?: object}>}
 */
export async function firebaseLogin(nis, password, pcName) {
  if (!db) {
    return { success: false, message: 'Firebase tidak tersedia.' };
  }

  try {
    // 1. Cari student berdasarkan NIS di Firestore
    const studentsRef = collection(db, 'students');
    const q = query(studentsRef, where('nis', '==', nis), limit(1));
    const snapshot = await getDocs(q);

    if (snapshot.empty) {
      return { success: false, message: 'NIS tidak ditemukan.' };
    }

    const studentDoc = snapshot.docs[0];
    const student = { id: studentDoc.id, ...studentDoc.data() };

    // 2. Cek apakah akun aktif
    if (!student.is_active || student.is_active === 0) {
      return { success: false, message: 'Akun siswa tidak aktif.' };
    }

    // 3. Verifikasi password menggunakan bcrypt via Electron main process
    if (window.electronAPI?.compareBcrypt) {
      const isValid = await window.electronAPI.compareBcrypt(password, student.password_hash);
      if (!isValid) {
        return { success: false, message: 'Password salah.' };
      }
    } else {
      // Di browser tanpa Electron, tidak bisa compare bcrypt di client
      return { success: false, message: 'Fallback login hanya tersedia di aplikasi Electron.' };
    }

    // 4. Login berhasil (tanpa membuat session di Firebase — session dibuat saat server online nanti)
    return {
      success: true,
      message: `Selamat datang, ${student.nama_lengkap}! (Mode Offline)`,
      data: {
        session_id: `offline_${Date.now()}`, // Temporary session ID
        student_id: student.id,
        nis: student.nis,
        nama_lengkap: student.nama_lengkap,
        kelas: student.kelas || '',
        pc_name: pcName,
        offline_mode: true, // Flag bahwa login via Firebase fallback
      },
    };
  } catch (error) {
    console.error('[Firebase Login] Error:', error);
    return { success: false, message: 'Gagal login via Firebase: ' + error.message };
  }
}

export { db, app };
