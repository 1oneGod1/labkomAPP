const crypto = require('crypto');

const TOKEN_TTL_MS = 8 * 60 * 60 * 1000; // 8 jam
const sessions = new Map(); // token -> { createdAt, expiresAt }

function issueToken() {
  const token = crypto.randomBytes(32).toString('hex');
  const now = Date.now();
  sessions.set(token, {
    createdAt: now,
    expiresAt: now + TOKEN_TTL_MS,
  });
  return token;
}

function validateToken(token) {
  if (!token) return false;
  const session = sessions.get(token);
  if (!session) return false;
  if (session.expiresAt < Date.now()) {
    sessions.delete(token);
    return false;
  }
  return true;
}

function getTokenExpiry(token) {
  const session = sessions.get(token);
  if (!session) return null;
  return session.expiresAt;
}

function rotateToken(oldToken) {
  if (!validateToken(oldToken)) return null;
  revokeToken(oldToken);
  return issueToken();
}

function revokeToken(token) {
  if (!token) return;
  sessions.delete(token);
}

module.exports = { issueToken, validateToken, getTokenExpiry, rotateToken, revokeToken, TOKEN_TTL_MS };
