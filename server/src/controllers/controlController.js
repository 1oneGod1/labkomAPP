const db = require('../config/database');

// ── GET /api/control/settings ────────────────────────────────────
async function getSettings(_req, res) {
  try {
    const [rows] = await db.query('SELECT setting_key, setting_value FROM control_settings');
    const settings = {};
    for (const row of rows) {
      try { settings[row.setting_key] = JSON.parse(row.setting_value); }
      catch { settings[row.setting_key] = row.setting_value; }
    }
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
    for (const [key, value] of Object.entries(updates)) {
      const strValue = typeof value === 'object' ? JSON.stringify(value) : String(value);
      await db.query(
        `INSERT INTO control_settings (setting_key, setting_value)
         VALUES (?, ?)
         ON DUPLICATE KEY UPDATE setting_value = ?, updated_at = NOW()`,
        [key, strValue, strValue]
      );
    }
    return res.json({ success: true, message: 'Pengaturan berhasil disimpan.' });
  } catch (err) {
    console.error('[CONTROL] updateSettings error:', err);
    return res.status(500).json({ success: false, message: 'Gagal menyimpan pengaturan.' });
  }
}

module.exports = { getSettings, updateSettings };
