const bcrypt     = require('bcrypt');
const db         = require('../config/database');
const os         = require('os');
const { getClientRegistry, normalizePcName } = require('../services/clientRegistryService');
const { resolveMappedLabPc } = require('../services/labComputerService');

// POST /api/auth/login
async function login(req, res) {
  const { nis, password, pc_name } = req.body;

  if (!nis || !password) {
    return res.status(400).json({ success: false, message: 'NIS dan password wajib diisi.' });
  }

  try {
    // 1. Cari siswa berdasarkan NIS
    const [rows] = await db.query(
      'SELECT * FROM students WHERE nis = ? LIMIT 1',
      [nis]
    );

    if (rows.length === 0) {
      return res.status(401).json({ success: false, message: 'NIS tidak ditemukan.' });
    }

    const student = rows[0];

    // 2. Cek apakah akun aktif
    if (!student.is_active) {
      return res.status(403).json({ success: false, message: 'Akun siswa tidak aktif.' });
    }

    // 3. Auto-close sesi hantu untuk PC yang sama (misal Electron crash sebelumnya)
    const reportedPcName = normalizePcName(pc_name || os.hostname());
    const presenceEntry = getClientRegistry().find(
      (entry) => normalizePcName(entry.pc_name) === reportedPcName
    );
    const mappedLabPc = await resolveMappedLabPc({
      pc_name: reportedPcName,
      mac: presenceEntry?.mac || null,
    });
    const pcName = mappedLabPc?.pc_name || reportedPcName;
    const cleanupPcNames = Array.from(new Set([reportedPcName, pcName].filter(Boolean)));
    const cleanupPlaceholders = cleanupPcNames.map(() => '?').join(', ');
    await db.query(
      `UPDATE sessions SET logout_time = NOW(), status = 'finished'
       WHERE pc_name IN (${cleanupPlaceholders}) AND status = 'active'`,
      cleanupPcNames
    );

    // 4. Cek apakah akun ini masih aktif di PC LAIN
    const [activeSessions] = await db.query(
      'SELECT * FROM sessions WHERE student_id = ? AND status = "active" LIMIT 1',
      [student.id]
    );

    if (activeSessions.length > 0) {
      return res.status(409).json({
        success: false,
        message: `Akun ini masih aktif di ${activeSessions[0].pc_name}. Hubungi guru untuk logout paksa.`,
      });
    }

    // 5. Verifikasi password
    const passwordValid = await bcrypt.compare(password, student.password_hash);
    if (!passwordValid) {
      return res.status(401).json({ success: false, message: 'Password salah.' });
    }

    // 6. Buat session baru
    const [result] = await db.query(
      'INSERT INTO sessions (student_id, pc_name, status) VALUES (?, ?, "active")',
      [student.id, pcName]
    );

    return res.status(200).json({
      success: true,
      message: `Selamat datang, ${student.nama_lengkap}!`,
      data: {
        session_id:   result.insertId,
        student_id:   student.id,
        nis:          student.nis,
        nama_lengkap: student.nama_lengkap,
        kelas:        student.kelas,
        pc_name:      pcName,
        actual_pc_name: reportedPcName,
      },
    });

  } catch (err) {
    console.error('[LOGIN ERROR]', err);
    return res.status(500).json({ success: false, message: 'Terjadi kesalahan server.' });
  }
}

// POST /api/auth/logout
async function logout(req, res) {
  const { session_id } = req.body;

  if (!session_id) {
    return res.status(400).json({ success: false, message: 'session_id wajib diisi.' });
  }

  try {
    const [result] = await db.query(
      `UPDATE sessions
       SET logout_time = NOW(), status = 'finished'
       WHERE id = ? AND status = 'active'`,
      [session_id]
    );

    if (result.affectedRows === 0) {
      return res.status(404).json({ success: false, message: 'Sesi tidak ditemukan atau sudah selesai.' });
    }

    return res.status(200).json({ success: true, message: 'Logout berhasil.' });

  } catch (err) {
    console.error('[LOGOUT ERROR]', err);
    return res.status(500).json({ success: false, message: 'Terjadi kesalahan server.' });
  }
}

// POST /api/auth/force-logout — paksa logout by student_id (untuk guru/admin)
async function forceLogout(req, res) {
  const { student_id, pc_name } = req.body;

  if (!student_id && !pc_name) {
    return res.status(400).json({ success: false, message: 'student_id atau pc_name wajib diisi.' });
  }

  try {
    let query, params;
    if (student_id) {
      query  = `UPDATE sessions SET logout_time = NOW(), status = 'force_ended' WHERE student_id = ? AND status = 'active'`;
      params = [student_id];
    } else {
      query  = `UPDATE sessions SET logout_time = NOW(), status = 'force_ended' WHERE pc_name = ? AND status = 'active'`;
      params = [pc_name];
    }
    const [result] = await db.query(query, params);
    return res.status(200).json({
      success: true,
      message: `${result.affectedRows} sesi berhasil di-logout paksa.`,
    });
  } catch (err) {
    console.error('[FORCE LOGOUT ERROR]', err);
    return res.status(500).json({ success: false, message: 'Terjadi kesalahan server.' });
  }
}

// GET /api/auth/status/:nis — cek real-time apakah siswa sedang login
async function checkStatus(req, res) {
  const { nis } = req.params;
  try {
    const [rows] = await db.query(
      `SELECT s.id, s.pc_name, s.login_time, s.status
       FROM sessions s
       JOIN students st ON s.student_id = st.id
       WHERE st.nis = ? AND s.status = 'active'
       LIMIT 1`,
      [nis]
    );
    if (rows.length === 0) {
      return res.status(200).json({ success: true, is_online: false });
    }
    return res.status(200).json({
      success:    true,
      is_online:  true,
      session:    rows[0],
    });
  } catch (err) {
    console.error('[CHECK STATUS ERROR]', err);
    return res.status(500).json({ success: false, message: 'Terjadi kesalahan server.' });
  }
}

module.exports = { login, logout, forceLogout, checkStatus };
