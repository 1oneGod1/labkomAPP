const { isFirestoreAvailable } = require('./firebaseService');
const admin = require('firebase-admin');

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
    if (!isFirestoreAvailable()) {
      console.warn('[AUDIT] Firestore not available, skipping audit log.');
      return;
    }

    const db = admin.firestore();
    await db.collection('admin_audit_logs').add({
      action,
      method: req.method,
      path: req.originalUrl,
      status_code: statusCode,
      success,
      ip_address: getIp(req),
      user_agent: req.headers['user-agent'] || null,
      metadata: metadata || null,
      created_at: admin.firestore.FieldValue.serverTimestamp(),
    });
  } catch (err) {
    console.warn('[AUDIT] Gagal simpan log admin:', err.message);
  }
}

module.exports = { logAdminAction };
