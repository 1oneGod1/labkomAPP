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

function upsertScreen({ pc_name, image, student_name = null }) {
  const normalizedPcName = normalizePcName(pc_name);
  if (!normalizedPcName || !image) return null;

  const next = {
    pc_name: normalizedPcName,
    student_name: student_name || null,
    image,
    updated_at: Date.now(),
  };

  screenshots.set(normalizedPcName, next);
  pruneExpiredScreens();
  return next;
}

function removeScreen(pcName) {
  const normalizedPcName = normalizePcName(pcName);
  if (!normalizedPcName) return false;
  return screenshots.delete(normalizedPcName);
}

function getActiveScreens(now = Date.now()) {
  pruneExpiredScreens(now);
  return Array.from(screenshots.values()).sort((a, b) => a.pc_name.localeCompare(b.pc_name));
}

module.exports = {
  SCREEN_TTL_MS,
  upsertScreen,
  removeScreen,
  getActiveScreens,
};
