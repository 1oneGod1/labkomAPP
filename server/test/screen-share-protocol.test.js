const test = require('node:test');
const assert = require('node:assert/strict');
const {
  MAX_SHOW_FRAME_BYTES,
  normalizeShowTargets,
  toFrameBuffer,
  validateShowFrame,
} = require('../src/services/screenShareProtocol');

const normalizePcName = (value) => String(value || '').trim().toUpperCase();

test('show targets support all, normalize selected PCs, and remove duplicates', () => {
  assert.equal(normalizeShowTargets('all', normalizePcName), null);
  assert.deepEqual(
    Array.from(normalizeShowTargets([' pc-01 ', 'PC-01', 'pc-02'], normalizePcName)),
    ['PC-01', 'PC-02'],
  );
  assert.equal(normalizeShowTargets('invalid', normalizePcName).size, 0);
});

test('binary frame conversion accepts Buffer, ArrayBuffer, and Uint8Array', () => {
  assert.deepEqual(toFrameBuffer(Buffer.from([1, 2])), Buffer.from([1, 2]));
  assert.deepEqual(toFrameBuffer(Uint8Array.from([3, 4]).buffer), Buffer.from([3, 4]));
  assert.deepEqual(toFrameBuffer(Uint8Array.from([5, 6])), Buffer.from([5, 6]));
});

test('frame validation clamps metadata and applies a safe MIME fallback', () => {
  const result = validateShowFrame({
    frame: Buffer.from([1, 2, 3]),
    mime: 'text/html',
    width: 99999,
    height: -4,
    sequence: 7,
    sent_at: 123,
  });
  assert.equal(result.ok, true);
  assert.equal(result.packet.mime, 'image/jpeg');
  assert.equal(result.packet.width, 4096);
  assert.equal(result.packet.height, 1);
  assert.equal(result.packet.sequence, 7);
  assert.equal(result.packet.sent_at, 123);
});

test('frame validation rejects empty and oversized payloads', () => {
  assert.equal(validateShowFrame({ frame: Buffer.alloc(0) }).status, 400);
  assert.equal(validateShowFrame({ frame: Buffer.alloc(MAX_SHOW_FRAME_BYTES + 1) }).status, 413);
});
