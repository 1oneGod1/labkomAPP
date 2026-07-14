const test = require('node:test');
const assert = require('node:assert/strict');

const clientTokens = require('../src/services/clientTokenService');
const { requireDevice } = require('../src/middleware/requireClient');
const adminSessions = require('../src/services/adminSessionService');

function responseRecorder() {
  return {
    statusCode: 200,
    body: null,
    status(code) { this.statusCode = code; return this; },
    json(body) { this.body = body; return this; },
  };
}

test('device registration validates identifiers and prevents a second device claiming a PC', () => {
  assert.equal(clientTokens.issueToken({ device_id: 'bad', pc_name: 'PC-01' }).ok, false);

  const first = clientTokens.issueToken({
    device_id: '11111111111111111111111111111111',
    pc_name: 'PC-TEST-01',
  });
  assert.equal(first.ok, true);
  assert.equal(clientTokens.validateToken(first.token).pc_name, 'PC-TEST-01');

  const conflict = clientTokens.issueToken({
    device_id: '22222222222222222222222222222222',
    pc_name: 'PC-TEST-01',
  });
  assert.equal(conflict.ok, false);
});

test('requireDevice binds actor identity to the token claim', () => {
  const issued = clientTokens.issueToken({
    device_id: '33333333333333333333333333333333',
    pc_name: 'pc-test-02',
  });
  const req = { headers: { authorization: `Bearer ${issued.token}` } };
  const res = responseRecorder();
  let called = false;

  requireDevice(req, res, () => { called = true; });

  assert.equal(called, true);
  assert.deepEqual(req.actor, {
    role: 'client',
    device_id: '33333333333333333333333333333333',
    pc_name: 'PC-TEST-02',
  });
});

test('requireDevice rejects an invalid token', () => {
  const req = { headers: { authorization: 'Bearer invalid' } };
  const res = responseRecorder();
  let called = false;

  requireDevice(req, res, () => { called = true; });

  assert.equal(called, false);
  assert.equal(res.statusCode, 401);
});
test('expired device token is rejected and releases its PC claim', () => {
  const originalNow = Date.now;
  let now = originalNow();
  Date.now = () => now;

  try {
    const first = clientTokens.issueToken({
      device_id: '44444444444444444444444444444444',
      pc_name: 'PC-TEST-03',
    });
    assert.equal(first.ok, true);

    now += 31 * 24 * 60 * 60 * 1000;
    assert.equal(clientTokens.validateToken(first.token), null);

    const replacement = clientTokens.issueToken({
      device_id: '55555555555555555555555555555555',
      pc_name: 'PC-TEST-03',
    });
    assert.equal(replacement.ok, true);
  } finally {
    Date.now = originalNow;
    clientTokens.revokePcClaim('PC-TEST-03');
  }
});

test('requireDevice does not accept an admin session token', () => {
  const adminToken = adminSessions.issueToken();
  const req = { headers: { authorization: `Bearer ${adminToken}` } };
  const res = responseRecorder();
  let called = false;

  try {
    requireDevice(req, res, () => { called = true; });
    assert.equal(called, false);
    assert.equal(res.statusCode, 401);
  } finally {
    adminSessions.revokeToken(adminToken);
  }
});