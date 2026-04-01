const db = require('../config/database');

// ── POST /api/checks ──────────────────────────────────────────────────────
// Catat hasil checklist fasilitas dari klien (pre atau post sesi)
async function submitCheck(req, res) {
  const {
    session_id, nis, nama_lengkap, pc_name, check_type,
    // Pre-check
    cpu_status, cpu_note,
    monitor_status, monitor_note,
    desk_status, desk_note,
    // Post-check
    hw_status, hw_note,
    cleanliness_status, cleanliness_note,
    account_status, account_note,
    system_status, system_note,
    file_status, file_note,
  } = req.body;

  if (!nis || !nama_lengkap || !pc_name || !check_type) {
    return res.status(400).json({ success: false, message: 'Field wajib tidak lengkap.' });
  }
  if (!['pre', 'post'].includes(check_type)) {
    return res.status(400).json({ success: false, message: 'check_type harus pre atau post.' });
  }

  try {
    const [result] = await db.query(`
      INSERT INTO facility_checks
        (session_id, nis, nama_lengkap, pc_name, check_type,
         cpu_status, cpu_note, monitor_status, monitor_note, desk_status, desk_note,
         hw_status, hw_note, cleanliness_status, cleanliness_note,
         account_status, account_note, system_status, system_note,
         file_status, file_note)
      VALUES (?, ?, ?, ?, ?,  ?, ?, ?, ?, ?, ?,  ?, ?, ?, ?,  ?, ?, ?, ?,  ?, ?)
    `, [
      session_id || null, nis, nama_lengkap, pc_name, check_type,
      cpu_status || null, cpu_note || null,
      monitor_status || null, monitor_note || null,
      desk_status || null, desk_note || null,
      hw_status || null, hw_note || null,
      cleanliness_status || null, cleanliness_note || null,
      account_status || null, account_note || null,
      system_status || null, system_note || null,
      file_status || null, file_note || null,
    ]);

    res.json({ success: true, id: result.insertId, message: 'Checklist berhasil dicatat.' });
  } catch (err) {
    console.error('[CHECKS] submitCheck error:', err);
    res.status(500).json({ success: false, message: 'Gagal menyimpan checklist.' });
  }
}

// ── GET /api/checks?date=YYYY-MM-DD&type=pre|post&page=1&limit=50 ──────────
// Ambil log pengecekan untuk Admin Dashboard
async function getChecks(req, res) {
  const { date, type, pc, page = 1, limit = 50 } = req.query;
  const offset = (parseInt(page) - 1) * parseInt(limit);

  try {
    const wheres = [];
    const params = [];

    if (date) { wheres.push(`DATE(fc.created_at) = ?`); params.push(date); }
    if (type && ['pre', 'post'].includes(type)) { wheres.push(`fc.check_type = ?`); params.push(type); }
    if (pc)   { wheres.push(`fc.pc_name LIKE ?`); params.push(`%${pc}%`); }

    const whereSQL = wheres.length ? `WHERE ${wheres.join(' AND ')}` : '';

    const [rows] = await db.query(`
      SELECT
        fc.id,
        fc.session_id,
        fc.nis,
        fc.nama_lengkap,
        fc.pc_name,
        fc.check_type,
        fc.cpu_status,    fc.cpu_note,
        fc.monitor_status, fc.monitor_note,
        fc.desk_status,   fc.desk_note,
        fc.hw_status,     fc.hw_note,
        fc.cleanliness_status, fc.cleanliness_note,
        fc.account_status,    fc.account_note,
        fc.system_status,     fc.system_note,
        fc.file_status,       fc.file_note,
        fc.created_at,
        -- Apakah ada minimal 1 item bermasalah?
        (
          fc.cpu_status = 'bad' OR fc.monitor_status = 'bad' OR fc.desk_status = 'bad' OR
          fc.hw_status = 'bad' OR fc.cleanliness_status = 'bad' OR  fc.account_status = 'bad' OR
          fc.system_status = 'bad' OR fc.file_status = 'bad'
        ) AS has_issue
      FROM facility_checks fc
      ${whereSQL}
      ORDER BY fc.created_at DESC
      LIMIT ? OFFSET ?
    `, [...params, parseInt(limit), offset]);

    const [[{ total }]] = await db.query(
      `SELECT COUNT(*) as total FROM facility_checks fc ${whereSQL}`,
      params
    );

    res.json({
      success: true,
      data:  rows.map(r => ({
        ...r,
        has_issue: Boolean(r.has_issue),
        date_str:  new Date(r.created_at).toLocaleDateString('id-ID', { day: '2-digit', month: 'long', year: 'numeric' }),
        time_str:  new Date(r.created_at).toLocaleTimeString('id-ID', { hour: '2-digit', minute: '2-digit' }),
      })),
      total,
      page:  parseInt(page),
      limit: parseInt(limit),
    });
  } catch (err) {
    console.error('[CHECKS] getChecks error:', err);
    res.status(500).json({ success: false, message: 'Gagal mengambil data checklist.' });
  }
}

// ── GET /api/checks/summary?date=YYYY-MM-DD ───────────────────────────────
// Ringkasan jumlah issue per PC untuk banner di dashboard
async function getChecksSummary(req, res) {
  const { date } = req.query;
  const params = [];
  let whereSQL = '';
  if (date) { whereSQL = `WHERE DATE(created_at) = ?`; params.push(date); }

  try {
    const [rows] = await db.query(`
      SELECT
        pc_name,
        SUM(check_type = 'pre') AS pre_count,
        SUM(check_type = 'post') AS post_count,
        SUM(
          cpu_status = 'bad' OR monitor_status = 'bad' OR desk_status = 'bad' OR
          hw_status = 'bad' OR cleanliness_status = 'bad' OR account_status = 'bad' OR
          system_status = 'bad' OR file_status = 'bad'
        ) AS issue_count
      FROM facility_checks
      ${whereSQL}
      GROUP BY pc_name
      ORDER BY issue_count DESC
    `, params);

    res.json({ success: true, data: rows });
  } catch (err) {
    res.status(500).json({ success: false, message: 'Gagal mengambil ringkasan.' });
  }
}

module.exports = { submitCheck, getChecks, getChecksSummary };
