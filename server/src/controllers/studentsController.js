const bcrypt = require('bcrypt');
const firebaseService = require('../services/firebaseService');

// ══════════════════════════════════════════════════════════════════════════
// STUDENTS CONTROLLER - MIGRATED TO FIREBASE
// ══════════════════════════════════════════════════════════════════════════

// ── GET /api/students ────────────────────────────────────────────
async function getStudents(_req, res) {
  try {
    // Check if Firebase is available
    if (!firebaseService.isFirestoreAvailable()) {
      return res.status(503).json({ 
        success: false, 
        message: 'Database tidak tersedia. Silakan setup Firebase terlebih dahulu.' 
      });
    }

    const students = await firebaseService.students.getAll();
    
    // Remove password_hash dari response
    const sanitized = students.map(s => {
      const { password_hash, ...rest } = s;
      return rest;
    });

    return res.json({ success: true, data: sanitized });
  } catch (err) {
    console.error('[STUDENTS] getStudents error:', err);
    return res.status(500).json({ success: false, message: 'Gagal mengambil data siswa.' });
  }
}

// ── POST /api/students ───────────────────────────────────────────
async function createStudent(req, res) {
  const { nis, nama_lengkap, kelas, password } = req.body;

  if (!nis || !nama_lengkap || !password) {
    return res.status(400).json({ success: false, message: 'NIS, nama, dan password wajib diisi.' });
  }

  try {
    // Check if Firebase is available
    if (!firebaseService.isFirestoreAvailable()) {
      return res.status(503).json({ 
        success: false, 
        message: 'Database tidak tersedia. Silakan setup Firebase terlebih dahulu.' 
      });
    }

    // Hash password
    const password_hash = await bcrypt.hash(password, 10);

    // Create student via Firebase service
    const newStudent = await firebaseService.students.create({
      nis,
      nama_lengkap,
      kelas: kelas || null,
      password_hash,
      is_active: 1,
    });

    return res.status(201).json({
      success: true,
      message: 'Siswa berhasil ditambahkan.',
      data: newStudent,
    });
  } catch (err) {
    console.error('[STUDENTS] createStudent error:', err);
    
    // Handle specific errors
    if (err.message === 'NIS sudah terdaftar') {
      return res.status(409).json({ success: false, message: err.message });
    }
    
    return res.status(500).json({ success: false, message: 'Gagal menambahkan siswa.' });
  }
}

// ── PUT /api/students/:id ────────────────────────────────────────
async function updateStudent(req, res) {
  const { id } = req.params;
  const { nis, nama_lengkap, kelas, is_active, password } = req.body;

  try {
    // Check if Firebase is available
    if (!firebaseService.isFirestoreAvailable()) {
      return res.status(503).json({ 
        success: false, 
        message: 'Database tidak tersedia. Silakan setup Firebase terlebih dahulu.' 
      });
    }

    // Check if student exists
    const existing = await firebaseService.students.getById(id);
    if (!existing) {
      return res.status(404).json({ success: false, message: 'Siswa tidak ditemukan.' });
    }

    // Prepare update data
    const updateData = {
      nis,
      nama_lengkap,
      kelas,
      is_active,
    };

    // If password is provided, hash it
    if (password) {
      updateData.password_hash = await bcrypt.hash(password, 10);
    }

    // Update via Firebase service
    await firebaseService.students.update(id, updateData);

    return res.json({ success: true, message: 'Data siswa berhasil diperbarui.' });
  } catch (err) {
    console.error('[STUDENTS] updateStudent error:', err);
    return res.status(500).json({ success: false, message: 'Gagal memperbarui data siswa.' });
  }
}

// ── DELETE /api/students/:id ─────────────────────────────────────
// Soft delete - set is_active to 0
async function deleteStudent(req, res) {
  const { id } = req.params;

  try {
    // Check if Firebase is available
    if (!firebaseService.isFirestoreAvailable()) {
      return res.status(503).json({ 
        success: false, 
        message: 'Database tidak tersedia. Silakan setup Firebase terlebih dahulu.' 
      });
    }

    // Check if student exists
    const existing = await firebaseService.students.getById(id);
    if (!existing) {
      return res.status(404).json({ success: false, message: 'Siswa tidak ditemukan.' });
    }

    // Soft delete via Firebase service
    await firebaseService.students.delete(id);

    return res.json({ success: true, message: 'Akun siswa berhasil dinonaktifkan.' });
  } catch (err) {
    console.error('[STUDENTS] deleteStudent error:', err);
    return res.status(500).json({ success: false, message: 'Gagal menonaktifkan siswa.' });
  }
}

// ── POST /api/students/import-batch ──────────────────────────────
async function importBatch(req, res) {
  const { students } = req.body;

  if (!Array.isArray(students) || students.length === 0) {
    return res.status(400).json({
      success: false,
      message: 'Data siswa untuk di-import tidak boleh kosong.',
    });
  }

  try {
    if (!firebaseService.isFirestoreAvailable()) {
      return res.status(503).json({
        success: false,
        message: 'Database tidak tersedia. Silakan setup Firebase terlebih dahulu.',
      });
    }

    let importedCount = 0;
    let updatedCount = 0;
    let failedCount = 0;
    const errors = [];

    for (let index = 0; index < students.length; index++) {
      const item = students[index];
      const rowNum = index + 1;

      const nis = String(item.nis || '').trim();
      const nama_lengkap = String(item.nama_lengkap || item.nama || '').trim();
      const kelas = String(item.kelas || '').trim();
      const password = String(item.password || '').trim();

      if (!nis || !nama_lengkap) {
        failedCount++;
        errors.push(`Baris ${rowNum}: NIS dan Nama Lengkap wajib diisi.`);
        continue;
      }

      try {
        const existing = await firebaseService.students.getByNis(nis);
        if (existing) {
          const updateData = {
            nis,
            nama_lengkap,
            kelas: kelas || existing.kelas || null,
            is_active: 1,
          };
          if (password) {
            updateData.password_hash = await bcrypt.hash(password, 10);
          }
          await firebaseService.students.update(existing.id, updateData);
          updatedCount++;
        } else {
          if (!password) {
            failedCount++;
            errors.push(`Baris ${rowNum} (NIS ${nis}): Password wajib diisi untuk siswa baru.`);
            continue;
          }
          const password_hash = await bcrypt.hash(password, 10);
          await firebaseService.students.create({
            nis,
            nama_lengkap,
            kelas: kelas || null,
            password_hash,
            is_active: 1,
          });
          importedCount++;
        }
      } catch (err) {
        failedCount++;
        errors.push(`Baris ${rowNum} (NIS ${nis}): ${err.message || 'Gagal menyimpan data.'}`);
      }
    }

    return res.json({
      success: true,
      message: `Proses import selesai. ${importedCount} dibuat baru, ${updatedCount} di-update, ${failedCount} gagal.`,
      importedCount,
      updatedCount,
      failedCount,
      totalProcessed: students.length,
      errors,
    });
  } catch (err) {
    console.error('[STUDENTS] importBatch error:', err);
    return res.status(500).json({ success: false, message: 'Gagal memproses import data siswa.' });
  }
}

// ── GET /api/students/template ───────────────────────────────────
async function downloadTemplate(_req, res) {
  try {
    const csvContent = '\uFEFF' + [
      'nis,nama_lengkap,kelas,password',
      '1001,Ahmad Fauzi,X-IPA-1,siswa123',
      '1002,Budi Santoso,X-IPA-1,siswa123',
      '1003,Citra Dewi,X-IPS-2,siswa123',
    ].join('\r\n');

    res.setHeader('Content-Type', 'text/csv; charset=utf-8');
    res.setHeader('Content-Disposition', 'attachment; filename="template_login_siswa.csv"');
    return res.status(200).send(csvContent);
  } catch (err) {
    console.error('[STUDENTS] downloadTemplate error:', err);
    return res.status(500).json({ success: false, message: 'Gagal mengunduh template.' });
  }
}

module.exports = {
  getStudents,
  createStudent,
  updateStudent,
  deleteStudent,
  importBatch,
  downloadTemplate,
};
