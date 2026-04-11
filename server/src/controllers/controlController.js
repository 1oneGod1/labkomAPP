const firebaseService = require('../services/firebaseService');

// ── GET /api/control/settings ────────────────────────────────────
async function getSettings(_req, res) {
  try {
    const settings = await firebaseService.control.getAll();
    return res.json({ success: true, data: settings });
  } catch (err) {
    console.error('[CONTROL] getSettings error:', err);
    return res.status(500).json({ success: false, message: 'Gagal mengambil pengaturan.' });
  }
}

// ── POST /api/control/settings ───────────────────────────────────
// body: objek key-value pengaturan yang akan diupdate
async function updateSettings(req, res) {
  const updates = req.body; // { key: value, ... }
  if (!updates || typeof updates !== 'object') {
    return res.status(400).json({ success: false, message: 'Body tidak valid.' });
  }

  try {
    // Convert values to strings for Firestore storage
    const data = {};
    for (const [key, value] of Object.entries(updates)) {
      data[key] = typeof value === 'object' ? JSON.stringify(value) : String(value);
    }
    await firebaseService.control.updateAll(data);
    return res.json({ success: true, message: 'Pengaturan berhasil disimpan.' });
  } catch (err) {
    console.error('[CONTROL] updateSettings error:', err);
    return res.status(500).json({ success: false, message: 'Gagal menyimpan pengaturan.' });
  }
}

module.exports = { getSettings, updateSettings };
