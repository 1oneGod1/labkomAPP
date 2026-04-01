const WINDOW_MS = 5 * 60 * 1000; // 5 menit
const MAX_ATTEMPTS = 5;
const BLOCK_MS = 15 * 60 * 1000; // 15 menit

const attempts = new Map(); // key -> { timestamps: number[], blockedUntil: number|null }

function nowMs() {
  return Date.now();
}

function getState(key) {
  const now = nowMs();
  const state = attempts.get(key) || { timestamps: [], blockedUntil: null };

  state.timestamps = state.timestamps.filter((ts) => now - ts <= WINDOW_MS);
  if (state.blockedUntil && state.blockedUntil <= now) {
    state.blockedUntil = null;
  }

  attempts.set(key, state);
  return state;
}

function checkAllowed(key) {
  const state = getState(key);
  if (state.blockedUntil && state.blockedUntil > nowMs()) {
    return {
      allowed: false,
      retryAfterSec: Math.ceil((state.blockedUntil - nowMs()) / 1000),
    };
  }
  return { allowed: true, retryAfterSec: 0 };
}

function registerFailure(key) {
  const state = getState(key);
  state.timestamps.push(nowMs());
  if (state.timestamps.length >= MAX_ATTEMPTS) {
    state.blockedUntil = nowMs() + BLOCK_MS;
    state.timestamps = [];
  }
  attempts.set(key, state);
}

function clearFailures(key) {
  attempts.delete(key);
}

module.exports = { checkAllowed, registerFailure, clearFailures };
