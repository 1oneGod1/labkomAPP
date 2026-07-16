const { validateShowFrame } = require('./screenShareProtocol');

const MAX_MONITORS = 8;

function clampInteger(value, minimum, maximum, fallback) {
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) return fallback;
  return Math.min(maximum, Math.max(minimum, Math.round(parsed)));
}

function normalizeDisplayId(value) {
  const displayId = String(value ?? '').trim();
  return displayId ? displayId.slice(0, 64) : null;
}

function normalizeMonitors(monitors) {
  if (!Array.isArray(monitors)) return [];

  const seen = new Set();
  const normalized = [];
  for (const monitor of monitors) {
    if (normalized.length >= MAX_MONITORS) break;
    const displayId = normalizeDisplayId(monitor?.display_id ?? monitor?.id);
    if (!displayId || seen.has(displayId)) continue;
    seen.add(displayId);
    normalized.push({
      display_id: displayId,
      label: String(monitor?.label || `Monitor ${normalized.length + 1}`).slice(0, 80),
      width: clampInteger(monitor?.width, 1, 16384, 1),
      height: clampInteger(monitor?.height, 1, 16384, 1),
      scale_factor: Math.min(8, Math.max(0.25, Number(monitor?.scale_factor) || 1)),
      primary: Boolean(monitor?.primary),
    });
  }
  return normalized;
}

function validateStudentScreenFrame(payload = {}) {
  const validated = validateShowFrame({
    frame: payload.frame,
    mime: payload.mime,
    width: payload.width,
    height: payload.height,
    sequence: payload.sequence,
    sent_at: payload.captured_at,
  });
  if (!validated.ok) return validated;

  return {
    ok: true,
    packet: {
      ...validated.packet,
      captured_at: validated.packet.sent_at,
      display_id: normalizeDisplayId(payload.display_id),
      display_label: String(payload.display_label || '').slice(0, 80) || null,
      monitors: normalizeMonitors(payload.monitors),
      student_name: String(payload.student_name || '').slice(0, 120) || null,
    },
  };
}

module.exports = {
  MAX_MONITORS,
  normalizeDisplayId,
  normalizeMonitors,
  validateStudentScreenFrame,
};
