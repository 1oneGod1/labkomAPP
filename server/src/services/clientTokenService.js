const crypto = require('crypto');

const TOKEN_TTL_MS = 30 * 24 * 60 * 60 * 1000; // 30 hari

// token -> { device_id, pc_name, expiresAt }
const tokens = new Map();
// pc_name(uppercase) -> { device_id, token } — first-come-claim
const pcClaims = new Map();

function normalizePcName(name) {
  return String(name || '').trim().toUpperCase();
}

function issueToken({ device_id, pc_name }) {
  const pcKey = normalizePcName(pc_name);
  const normalizedDeviceId = String(device_id || '').trim().toLowerCase();
  if (!/^[a-f0-9]{32}$/.test(normalizedDeviceId)) {
    return { ok: false, message: 'device_id tidak valid.' };
  }
  if (!/^[A-Z0-9][A-Z0-9._-]{0,62}$/.test(pcKey)) {
    return { ok: false, message: 'pc_name tidak valid.' };
  }

  const existing = pcClaims.get(pcKey);
  if (existing && existing.device_id !== normalizedDeviceId) {
    const entry = tokens.get(existing.token);
    if (entry && entry.expiresAt >= Date.now()) {
      return { ok: false, message: `PC "${pcKey}" sudah diklaim device lain. Hubungi admin untuk reset device.` };
    }
    // expired claim — buang
    pcClaims.delete(pcKey);
    tokens.delete(existing.token);
  }

  // Reuse existing token kalau device_id sama dan belum expired
  if (existing && existing.device_id === normalizedDeviceId) {
    const entry = tokens.get(existing.token);
    if (entry && entry.expiresAt >= Date.now()) {
      return { ok: true, token: existing.token };
    }
  }

  const token = crypto.randomBytes(32).toString('hex');
  tokens.set(token, { device_id: normalizedDeviceId, pc_name: pcKey, expiresAt: Date.now() + TOKEN_TTL_MS });
  pcClaims.set(pcKey, { device_id: normalizedDeviceId, token });
  return { ok: true, token };
}

function validateToken(token) {
  if (!token) return null;
  const entry = tokens.get(token);
  if (!entry) return null;
  if (entry.expiresAt < Date.now()) {
    tokens.delete(token);
    pcClaims.delete(entry.pc_name);
    return null;
  }
  return entry;
}

function revokePcClaim(pc_name) {
  const pcKey = normalizePcName(pc_name);
  const claim = pcClaims.get(pcKey);
  if (!claim) return false;
  tokens.delete(claim.token);
  pcClaims.delete(pcKey);
  return true;
}

function listClaims() {
  return Array.from(pcClaims.entries()).map(([pc_name, c]) => {
    const entry = tokens.get(c.token);
    return {
      pc_name,
      device_id: c.device_id,
      expires_at: entry?.expiresAt || null,
    };
  });
}

module.exports = { issueToken, validateToken, revokePcClaim, listClaims };
