const test = require('node:test');
const assert = require('node:assert/strict');

const {
  authorizeRegistration,
  MIN_REGISTRATION_KEY_LENGTH,
} = require('../src/services/registrationKeyService');

test('production requires a registration key of at least 32 characters', () => {
  const missing = authorizeRegistration({
    configuredKey: '',
    suppliedKey: '',
    isProduction: true,
  });
  assert.equal(missing.ok, false);
  assert.equal(missing.status, 503);

  const tooShort = authorizeRegistration({
    configuredKey: 'short-key',
    suppliedKey: 'short-key',
    isProduction: true,
  });
  assert.equal(tooShort.ok, false);
  assert.equal(tooShort.status, 503);
});

test('configured registration key must match exactly', () => {
  const configuredKey = 'a'.repeat(MIN_REGISTRATION_KEY_LENGTH);

  const rejected = authorizeRegistration({
    configuredKey,
    suppliedKey: `${configuredKey}x`,
    isProduction: true,
  });
  assert.equal(rejected.ok, false);
  assert.equal(rejected.status, 403);

  const accepted = authorizeRegistration({
    configuredKey,
    suppliedKey: configuredKey,
    isProduction: true,
  });
  assert.deepEqual(accepted, { ok: true });
});

test('development may run without a registration key', () => {
  assert.deepEqual(authorizeRegistration({
    configuredKey: '',
    suppliedKey: '',
    isProduction: false,
  }), { ok: true });
});
