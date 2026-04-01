const db = require('../config/database');

// GET /api/sessions — semua sesi (untuk dashboard admin)
async function getAllSessions(req, res) {
  try {
    const [rows] = await db.query(`
      SELECT
        s.id,
        s.pc_name,
        s.login_time,
        s.logout_time,
        s.status,
        st.nis,
        st.nama_lengkap,
        st.kelas
      FROM sessions s
      JOIN students st ON s.student_id = st.id
      ORDER BY s.login_time DESC
      LIMIT 100
    `);
    return res.status(200).json({ success: true, data: rows });
  } catch (err) {
    console.error('[GET SESSIONS ERROR]', err);
    return res.status(500).json({ success: false, message: 'Terjadi kesalahan server.' });
  }
}

// GET /api/sessions/active — hanya sesi yang sedang aktif
async function getActiveSessions(req, res) {
  try {
    const [rows] = await db.query(`
      SELECT
        s.id,
        s.pc_name,
        s.login_time,
        st.nis,
        st.nama_lengkap,
        st.kelas
      FROM sessions s
      JOIN students st ON s.student_id = st.id
      WHERE s.status = 'active'
      ORDER BY s.login_time ASC
    `);
    return res.status(200).json({ success: true, data: rows });
  } catch (err) {
    console.error('[GET ACTIVE SESSIONS ERROR]', err);
    return res.status(500).json({ success: false, message: 'Terjadi kesalahan server.' });
  }
}

module.exports = { getAllSessions, getActiveSessions };
