const test = require('node:test');
const assert = require('node:assert/strict');
const {
  MAX_MONITORS,
  normalizeDisplayId,
  normalizeMonitors,
  validateStudentScreenFrame,
} = require('../src/services/studentScreenProtocol');

test('display identifiers and monitor metadata are normalized and bounded', () => {
  assert.equal(normalizeDisplayId(' 123 '), '123');
  assert.equal(normalizeDisplayId(''), null);

  const monitors = normalizeMonitors([
    { id: 1, label: 'Primary', width: 1920, height: 1080, primary: true },
    { display_id: '1', label: 'Duplicate' },
    ...Array.from({ length: 12 }, (_, index) => ({ id: index + 2 })),
  ]);
  assert.equal(monitors.length, MAX_MONITORS);
  assert.equal(monitors[0].display_id, '1');
  assert.equal(monitors[0].primary, true);
});

test('student screen frame preserves safe display metadata', () => {
  const result = validateStudentScreenFrame({
    frame: Buffer.from([1, 2, 3]),
    mime: 'image/jpeg',
    width: 1280,
    height: 720,
    sequence: 9,
    captured_at: 1234,
    display_id: '42',
    display_label: 'Monitor 2',
    student_name: 'Siswa',
    monitors: [{ id: '42', width: 1920, height: 1080 }],
  });

  assert.equal(result.ok, true);
  assert.equal(result.packet.display_id, '42');
  assert.equal(result.packet.display_label, 'Monitor 2');
  assert.equal(result.packet.sequence, 9);
  assert.equal(result.packet.captured_at, 1234);
  assert.equal(result.packet.monitors.length, 1);
});

test('student screen frame rejects an empty binary payload', () => {
  assert.equal(validateStudentScreenFrame({ frame: Buffer.alloc(0) }).status, 400);
});
