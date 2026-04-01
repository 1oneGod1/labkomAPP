const ONLINE_TTL_MS = 15000;
const clientRegistry = new Map();

function normalizePcName(pcName) {
  return String(pcName || '').trim().toUpperCase();
}

function normalizeMac(mac) {
  return String(mac || '')
    .trim()
    .replace(/-/g, ':')
    .toUpperCase();
}

function upsertClient(entry = {}) {
  const normalizedPcName = normalizePcName(entry.pc_name);
  if (!normalizedPcName) return null;

  const now = Date.now();
  const existing = clientRegistry.get(normalizedPcName) || {
    pc_name: normalizedPcName,
    mac: null,
    ip: null,
    student_name: null,
    socket_id: null,
    source: null,
    first_seen: now,
    last_seen: now,
    connected: false,
  };

  const next = {
    ...existing,
    pc_name: normalizedPcName,
    last_seen: now,
    connected: true,
  };

  if (entry.mac !== undefined) next.mac = normalizeMac(entry.mac) || existing.mac || null;
  if (entry.ip !== undefined) next.ip = entry.ip || existing.ip || null;
  if (entry.student_name !== undefined) next.student_name = entry.student_name || null;
  if (entry.socket_id !== undefined) next.socket_id = entry.socket_id || null;
  if (entry.source !== undefined) next.source = entry.source || null;

  clientRegistry.set(normalizedPcName, next);
  return { ...next, is_online: true };
}

function markClientDisconnected(pcName, socketId = null) {
  const normalizedPcName = normalizePcName(pcName);
  if (!normalizedPcName) return null;

  const existing = clientRegistry.get(normalizedPcName);
  if (!existing) return null;
  if (socketId && existing.socket_id && existing.socket_id !== socketId) return existing;

  const next = {
    ...existing,
    connected: false,
    socket_id: null,
    last_seen: Date.now(),
  };

  clientRegistry.set(normalizedPcName, next);
  return { ...next, is_online: false };
}

function getClientRegistry(now = Date.now()) {
  return Array.from(clientRegistry.values(), (entry) => ({
    ...entry,
    is_online: entry.connected || (now - entry.last_seen < ONLINE_TTL_MS),
  })).sort((a, b) => a.pc_name.localeCompare(b.pc_name));
}

function getOnlineClientMap(now = Date.now()) {
  const map = new Map();
  for (const entry of getClientRegistry(now)) {
    if (entry.is_online) {
      map.set(entry.pc_name, entry);
    }
  }
  return map;
}

module.exports = {
  ONLINE_TTL_MS,
  normalizePcName,
  normalizeMac,
  upsertClient,
  markClientDisconnected,
  getClientRegistry,
  getOnlineClientMap,
};
