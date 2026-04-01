const db = require('../config/database');

// ── GET /api/sessions/history?date=YYYY-MM-DD&page=1&limit=50 ───
async function getHistory(req, res) {
  const { date, page = 1, limit = 50 } = req.query;
  const offset = (parseInt(page) - 1) * parseInt(limit);

  try {
    let whereClauses = [];
    let params       = [];

    if (date) {
      whereClauses.push(`DATE(s.login_time) = ?`);
      params.push(date);
    }

    const whereSQL = whereClauses.length > 0 ? `WHERE ${whereClauses.join(' AND ')}` : '';

    const [rows] = await db.query(`
      SELECT
        s.id,
        s.pc_name,
        s.login_time,
        s.logout_time,
        s.status,
        TIMESTAMPDIFF(MINUTE, s.login_time, IFNULL(s.logout_time, NOW())) AS duration_minutes,
        st.nis,
        st.nama_lengkap,
        st.kelas
      FROM sessions s
      JOIN students st ON st.id = s.student_id
      ${whereSQL}
      ORDER BY s.login_time DESC
      LIMIT ? OFFSET ?
    `, [...params, parseInt(limit), offset]);

    const [[{ total }]] = await db.query(
      `SELECT COUNT(*) as total FROM sessions s ${whereSQL}`,
      params
    );

    const history = rows.map(r => {
      const h = Math.floor(r.duration_minutes / 60);
      const m = r.duration_minutes % 60;
      const durStr = h > 0 ? `${h}j ${m}m` : `${m}m`;

      let sessionType = 'Selesai Normal';
      if (r.status === 'active') sessionType = 'Sedang Berlangsung';
      else if (r.status === 'force_ended') sessionType = 'Dipaksa Keluar (Admin)';

      return {
        id:         r.id,
        date:       new Date(r.login_time).toLocaleDateString('id-ID', { day: '2-digit', month: 'long', year: 'numeric' }),
        pc:         r.pc_name,
        nis:        r.nis,
        name:       r.nama_lengkap,
        kelas:      r.kelas,
        login:      new Date(r.login_time).toLocaleTimeString('id-ID', { hour: '2-digit', minute: '2-digit' }),
        logout:     r.logout_time
          ? new Date(r.logout_time).toLocaleTimeString('id-ID', { hour: '2-digit', minute: '2-digit' })
          : '-',
        duration:   durStr,
        type:       sessionType,
        status:     r.status,
      };
    });

    return res.json({ success: true, data: history, total, page: parseInt(page), limit: parseInt(limit) });
  } catch (err) {
    console.error('[SESSIONS] getHistory error:', err);
    return res.status(500).json({ success: false, message: 'Gagal mengambil riwayat sesi.' });
  }
}

module.exports = { getHistory };
