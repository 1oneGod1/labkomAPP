const crypto = require('crypto');

const MIN_REGISTRATION_KEY_LENGTH = 32;

function constantTimeEqual(left, right) {
  const leftDigest = crypto.createHash('sha256').update(String(left || ''), 'utf8').digest();
  const rightDigest = crypto.createHash('sha256').update(String(right || ''), 'utf8').digest();
  return crypto.timingSafeEqual(leftDigest, rightDigest);
}

function authorizeRegistration({ configuredKey, suppliedKey, isProduction }) {
  const key = typeof configuredKey === 'string' ? configuredKey : '';

  if (isProduction && key.length < MIN_REGISTRATION_KEY_LENGTH) {
    return {
      ok: false,
      status: 503,
      message: `Kunci registrasi server wajib minimal ${MIN_REGISTRATION_KEY_LENGTH} karakter.`,
    };
  }

  if (key && !constantTimeEqual(suppliedKey, key)) {
    return {
      ok: false,
      status: 403,
      message: 'Kunci registrasi perangkat tidak valid.',
    };
  }

  return { ok: true };
}

module.exports = { authorizeRegistration, MIN_REGISTRATION_KEY_LENGTH };
