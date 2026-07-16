const MAX_SHOW_FRAME_BYTES = 1536 * 1024;
const ALLOWED_FRAME_MIME = new Set(['image/jpeg', 'image/webp']);

function normalizeShowTargets(targets, normalizePcName) {
  if (targets === 'all' || targets === undefined || targets === null) return null;
  if (!Array.isArray(targets)) return new Set();
  return new Set(targets.slice(0, 200).map(normalizePcName).filter(Boolean));
}

function toFrameBuffer(frame) {
  if (Buffer.isBuffer(frame)) return frame;
  if (frame instanceof ArrayBuffer) return Buffer.from(frame);
  if (ArrayBuffer.isView(frame)) return Buffer.from(frame.buffer, frame.byteOffset, frame.byteLength);
  if (frame?.type === 'Buffer' && Array.isArray(frame.data)) return Buffer.from(frame.data);
  return null;
}

function clampInteger(value, min, max, fallback) {
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) return fallback;
  return Math.max(min, Math.min(Math.round(parsed), max));
}

function validateShowFrame(data = {}, now = Date.now()) {
  const frame = toFrameBuffer(data.frame);
  if (!frame || frame.length === 0) {
    return { ok: false, status: 400, message: 'Frame kosong atau tidak valid.' };
  }
  if (frame.length > MAX_SHOW_FRAME_BYTES) {
    return { ok: false, status: 413, message: 'Frame terlalu besar.' };
  }

  return {
    ok: true,
    packet: {
      frame,
      mime: ALLOWED_FRAME_MIME.has(data.mime) ? data.mime : 'image/jpeg',
      width: clampInteger(data.width, 1, 4096, 1),
      height: clampInteger(data.height, 1, 2160, 1),
      sequence: clampInteger(data.sequence, 0, Number.MAX_SAFE_INTEGER, 0),
      sent_at: clampInteger(data.sent_at, 0, Number.MAX_SAFE_INTEGER, now),
    },
  };
}

module.exports = {
  MAX_SHOW_FRAME_BYTES,
  normalizeShowTargets,
  toFrameBuffer,
  validateShowFrame,
};
