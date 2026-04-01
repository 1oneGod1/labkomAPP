const mysql2 = require('mysql2/promise');
require('dotenv').config();

const pool = mysql2.createPool({
  host:     process.env.DB_HOST || 'localhost',
  port:     process.env.DB_PORT || 3306,
  user:     process.env.DB_USER || 'root',
  password: process.env.DB_PASSWORD || '',
  database: process.env.DB_NAME || 'labkom_db',
  waitForConnections: true,
  connectionLimit: 10,
});

// Test koneksi saat startup
async function testConnection() {
  try {
    const conn = await pool.getConnection();
    console.log('[DB] Koneksi MySQL berhasil!');
    conn.release();
  } catch (err) {
    console.error('[DB] Gagal konek ke MySQL:', err.message);
    process.exit(1);
  }
}

testConnection();

module.exports = pool;
