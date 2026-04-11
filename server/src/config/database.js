const mysql2 = require('mysql2/promise');
require('dotenv').config();

let pool = null;

try {
  pool = mysql2.createPool({
    host:     process.env.DB_HOST || 'localhost',
    port:     process.env.DB_PORT || 3306,
    user:     process.env.DB_USER || 'root',
    password: process.env.DB_PASSWORD || '',
    database: process.env.DB_NAME || 'labkom_db',
    waitForConnections: true,
    connectionLimit: 10,
  });
} catch (err) {
  console.warn('[DB] ⚠️  Gagal membuat MySQL pool:', err.message);
  console.log('[DB] Aplikasi tetap berjalan menggunakan Firebase.');
}

// Test koneksi saat startup (non-blocking, tidak exit jika gagal)
async function testConnection() {
  if (!pool) {
    console.warn('[DB] ⚠️  MySQL pool tidak tersedia. Menggunakan Firebase saja.');
    return;
  }
  try {
    const conn = await pool.getConnection();
    console.log('[DB] ✅ Koneksi MySQL berhasil!');
    conn.release();
  } catch (err) {
    console.warn('[DB] ⚠️  Gagal konek ke MySQL:', err.message);
    console.log('[DB] Aplikasi tetap berjalan menggunakan Firebase sebagai database utama.');
    // TIDAK process.exit(1) — biarkan server tetap jalan
    pool = null; // set null agar controller tahu MySQL tidak tersedia
  }
}

testConnection();

module.exports = pool;
