const { normalizePcName } = require('./clientRegistryService');

const SCREEN_TTL_MS = 12000;
const screenshots = new Map();

function pruneExpiredScreens(now = Date.now()) {
  for (const [key, value] of screenshots) {
    if (now - value.updated_at >= SCREEN_TTL_MS) {
      screenshots.delete(key);
    }
  }
}

function upsertScreen({
  pc_name,
  image = null,
  frame = null,
  mime = 'image/jpeg',
  student_name = null,
  width = null,
  height = null,
  sequence = null,
  captured_at = null,
  display_id = null,
  display_label = null,
  monitors = [],
}) {
  const normalizedPcName = normalizePcName(pc_name);
  const binaryFrame = frame ? Buffer.from(frame) : null;
  if (!normalizedPcName || (!image && !binaryFrame?.length)) return null;

  const next = {
    pc_name: normalizedPcName,
    student_name: student_name || null,
    image: image || null,
    frame: binaryFrame,
    mime,
    width,
    height,
    sequence,
    captured_at,
    display_id,
    display_label,
    monitors: Array.isArray(monitors) ? monitors : [],
    updated_at: Date.now(),
  };

  screenshots.set(normalizedPcName, next);
  pruneExpiredScreens();
  return next;
}

function toLegacyScreen(screen) {
  if (!screen) return null;
  const image = screen.image || (screen.frame?.length
    ? `data:${screen.mime || 'image/jpeg'};base64,${screen.frame.toString('base64')}`
    : null);
  if (!image) return null;

  return {
    pc_name: screen.pc_name,
    student_name: screen.student_name,
    image,
    width: screen.width,
    height: screen.height,
    sequence: screen.sequence,
    captured_at: screen.captured_at,
    display_id: screen.display_id,
    display_label: screen.display_label,
    monitors: screen.monitors,
    updated_at: screen.updated_at,
  };
}

function removeScreen(pcName) {
  const normalizedPcName = normalizePcName(pcName);
  if (!normalizedPcName) return false;
  return screenshots.delete(normalizedPcName);
}

function getActiveScreens(now = Date.now()) {
  pruneExpiredScreens(now);
  return Array.from(screenshots.values())
    .map(toLegacyScreen)
    .filter(Boolean)
    .sort((a, b) => a.pc_name.localeCompare(b.pc_name));
}

module.exports = {
  SCREEN_TTL_MS,
  upsertScreen,
  toLegacyScreen,
  removeScreen,
  getActiveScreens,
};
