const db = require('../config/database');

let warnedMissingTable = false;

function getIp(req) {
  const xff = req.headers['x-forwarded-for'];
  if (typeof xff === 'string' && xff.trim()) {
    return xff.split(',')[0].trim();
  }
  return req.ip || req.socket?.remoteAddress || null;
}

async function logAdminAction(req, payload = {}) {
  const {
    action = `${req.method} ${req.originalUrl}`,
    statusCode = null,
    success = null,
    metadata = null,
  } = payload;

  try {
    await db.query(
      `INSERT INTO admin_audit_logs
        (action, method, path, status_code, success, ip_address, user_agent, metadata_json)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
      [
        action,
        req.method,
        req.originalUrl,
        statusCode,
        success,
        getIp(req),
        req.headers['user-agent'] || null,
        metadata ? JSON.stringify(metadata) : null,
      ]
    );
  } catch (err) {
    if (!warnedMissingTable && (err.code === 'ER_NO_SUCH_TABLE' || err.errno === 1146)) {
      warnedMissingTable = true;
      console.warn('[AUDIT] Tabel admin_audit_logs belum ada. Jalankan migration-v3-security.sql.');
      return;
    }
    console.warn('[AUDIT] Gagal simpan log admin:', err.message);
  }
}

module.exports = { logAdminAction };
