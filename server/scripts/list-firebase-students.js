/**
 * Script untuk menampilkan semua akun siswa yang tersimpan di Firebase Firestore
 * Jalankan: node scripts/list-firebase-students.js
 */

// Load environment variables
require('dotenv').config();

const { db } = require('../src/config/firebase');

async function listStudents() {
  if (!db) {
    console.error('❌ Firebase Firestore tidak tersedia!');
    console.log('Pastikan FIREBASE_SERVICE_ACCOUNT_KEY sudah di-set di .env');
    console.log('dan file firebase-service-account.json ada di folder server/');
    process.exit(1);
  }

  console.log('📋 Mengambil data siswa dari Firebase Firestore...\n');

  try {
    const snapshot = await db.collection('students').orderBy('nis').get();

    if (snapshot.empty) {
      console.log('⚠️  Tidak ada akun siswa yang ditemukan di Firebase.');
      console.log('   Collection "students" kosong.');
      process.exit(0);
    }

    console.log(`✅ Ditemukan ${snapshot.size} akun siswa:\n`);
    console.log('─'.repeat(90));
    console.log(
      'No'.padEnd(5) +
      'NIS'.padEnd(15) +
      'Nama Lengkap'.padEnd(30) +
      'Kelas'.padEnd(12) +
      'Aktif'.padEnd(8) +
      'Doc ID'
    );
    console.log('─'.repeat(90));

    let no = 1;
    snapshot.docs.forEach((doc) => {
      const data = doc.data();
      console.log(
        String(no).padEnd(5) +
        (data.nis || '-').padEnd(15) +
        (data.nama_lengkap || '-').padEnd(30) +
        (data.kelas || '-').padEnd(12) +
        (data.is_active === 1 || data.is_active === true ? '✅' : '❌').padEnd(8) +
        doc.id
      );
      no++;
    });

    console.log('─'.repeat(90));
    console.log(`\nTotal: ${snapshot.size} akun`);

    // Juga cek collection lain
    console.log('\n\n📊 Statistik Collection Firebase:');
    console.log('─'.repeat(40));

    const collections = ['students', 'sessions', 'lab_computers', 'facility_checks', 'control_settings'];
    for (const col of collections) {
      try {
        const snap = await db.collection(col).get();
        console.log(`  ${col.padEnd(25)} → ${snap.size} dokumen`);
      } catch (e) {
        console.log(`  ${col.padEnd(25)} → ❌ Error: ${e.message}`);
      }
    }

  } catch (error) {
    console.error('❌ Error mengambil data:', error.message);
  }

  process.exit(0);
}

listStudents();
