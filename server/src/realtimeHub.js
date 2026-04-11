const { Server } = require('socket.io');
const { validateToken } = require('./services/adminSessionService');
const {
  normalizePcName,
  upsertClient,
  markClientDisconnected,
} = require('./services/clientRegistryService');
const {
  upsertScreen,
  removeScreen,
  getActiveScreens,
} = require('./services/screenRelayService');
const firebaseService = require('./services/firebaseService');

const screenWatchers = new Map();

function getClientRoom(pcName) {
  const normalizedPcName = normalizePcName(pcName);
  return normalizedPcName ? `client:${normalizedPcName}` : null;
}

function updateWatcherCount(pcName, delta) {
  const normalizedPcName = normalizePcName(pcName);
  if (!normalizedPcName || !delta) return;

  const next = (screenWatchers.get(normalizedPcName) || 0) + delta;
  if (next <= 0) {
    screenWatchers.delete(normalizedPcName);
    return;
  }
  screenWatchers.set(normalizedPcName, next);
}

function emitScreenQuality(io, pcName) {
  const normalizedPcName = normalizePcName(pcName);
  const room = getClientRoom(normalizedPcName);
  if (!normalizedPcName || !room) return;

  const watcherCount = screenWatchers.get(normalizedPcName) || 0;
  io.to(room).emit('screen:quality', {
    pc_name: normalizedPcName,
    mode: watcherCount > 0 ? 'focus' : 'overview',
  });
}

function attachRealtimeHub(httpServer) {
  const io = new Server(httpServer, {
    cors: {
      origin: (origin, callback) => {
        // null origin = Electron file:// protocol
        if (!origin) return callback(null, true);
        const ALLOWED = [
          /^http:\/\/localhost(:\d+)?$/,
          /^http:\/\/127\.0\.0\.1(:\d+)?$/,
          /^http:\/\/192\.168\.\d{1,3}\.\d{1,3}(:\d+)?$/,
          /^http:\/\/10\.\d{1,3}\.\d{1,3}\.\d{1,3}(:\d+)?$/,
        ];
        if (ALLOWED.some(p => p.test(origin))) return callback(null, true);
        callback(new Error('Not allowed by CORS'));
      },
      credentials: true,
    },
    maxHttpBufferSize: 2 * 1024 * 1024,
  });

  io.use((socket, next) => {
    const role = socket.handshake.auth?.role;
    if (role === 'admin') {
      const token = socket.handshake.auth?.token;
      if (!validateToken(token)) {
        return next(new Error('unauthorized'));
      }
      socket.data.role = 'admin';
      return next();
    }

    socket.data.role = 'client';
    return next();
  });

  io.on('connection', (socket) => {
    if (socket.data.role === 'admin') {
      socket.join('admins');
      socket.emit('screens:snapshot', getActiveScreens());

      const setWatchTarget = (nextPcName = null) => {
        const previousPcName = normalizePcName(socket.data.watch_pc_name);
        const normalizedNextPcName = normalizePcName(nextPcName);
        if (previousPcName === normalizedNextPcName) return;

        if (previousPcName) {
          updateWatcherCount(previousPcName, -1);
          emitScreenQuality(io, previousPcName);
        }

        socket.data.watch_pc_name = normalizedNextPcName || null;

        if (normalizedNextPcName) {
          updateWatcherCount(normalizedNextPcName, 1);
          emitScreenQuality(io, normalizedNextPcName);
        }
      };

      socket.on('admin:watch-screen', ({ pc_name } = {}) => {
        setWatchTarget(pc_name || null);
      });

      socket.on('admin:stop-watch-screen', () => {
        setWatchTarget(null);
      });

      // ── Chat: Admin broadcast message to all clients ──────────
      socket.on('admin:broadcast-message', (data, callback) => {
        const { message: msg, timestamp: ts } = data || {};
        if (!msg) return callback?.({ success: false, error: 'Empty message' });

        const payload = {
          id: `msg_${Date.now()}_${Math.random().toString(36).substr(2, 6)}`,
          from: 'Admin',
          message: msg,
          timestamp: ts || new Date().toISOString(),
        };

        // Send to all non-admin sockets (clients)
        let count = 0;
        for (const [, s] of io.sockets.sockets) {
          if (s.data.role !== 'admin') {
            s.emit('chat:message-from-admin', payload);
            count++;
          }
        }

        // Save to Firebase (async, don't block)
        saveChatMessage({ ...payload, type: 'admin_broadcast', delivered_to: count }).catch(err => {
          console.error('[CHAT] Failed to save:', err.message);
        });

        callback?.({ success: true, count });
      });

      // ── Attention Mode (Blank Screen) ──────────────────────────
      socket.on('admin:attention-mode', ({ enabled, message, target } = {}) => {
        const payload = {
          enabled: Boolean(enabled),
          message: message || 'Mohon perhatian ke instruktur',
          timestamp: Date.now(),
        };

        if (target && target !== 'all') {
          // Send to specific PC
          const targetRoom = getClientRoom(target);
          if (targetRoom) {
            io.to(targetRoom).emit('attention-mode', payload);
          }
        } else {
          // Broadcast to all clients
          io.emit('attention-mode', payload);
        }

        // Notify other admins
        socket.to('admins').emit('attention-mode-status', {
          ...payload,
          target: target || 'all',
          admin_id: socket.id,
        });
      });

      socket.on('disconnect', () => {
        setWatchTarget(null);
      });

      return;
    }

    const bindClientRoom = (pcName) => {
      const normalizedPcName = normalizePcName(pcName);
      const previousPcName = normalizePcName(socket.data.pc_name);
      const previousRoom = getClientRoom(previousPcName);
      const nextRoom = getClientRoom(normalizedPcName);

      if (previousRoom && previousRoom !== nextRoom) {
        socket.leave(previousRoom);
      }
      if (nextRoom) {
        socket.join(nextRoom);
        socket.data.pc_name = normalizedPcName;
        emitScreenQuality(io, normalizedPcName);
      }
    };

    function updatePresence(payload = {}, source = 'socket') {
      const entry = upsertClient({
        pc_name: payload.pc_name,
        mac: payload.mac,
        ip: payload.ip,
        student_name: payload.student_name,
        socket_id: socket.id,
        source,
      });

      if (!entry) return null;
      bindClientRoom(entry.pc_name);
      io.to('admins').emit('presence:update', {
        pc_name: entry.pc_name,
        ip: entry.ip || null,
        mac: entry.mac || null,
        student_name: entry.student_name || null,
        is_online: true,
        last_seen: entry.last_seen,
      });
      return entry;
    }

    socket.on('client:hello', (payload = {}) => {
      updatePresence(payload, 'socket-hello');
    });

    socket.on('client:heartbeat', (payload = {}) => {
      updatePresence(payload, 'socket-heartbeat');
    });

    socket.on('client:screen', (payload = {}) => {
      const pcName = normalizePcName(payload.pc_name || socket.data.pc_name);
      if (!pcName || !payload.image) return;

      updatePresence({ ...payload, pc_name: pcName }, 'socket-screen');
      const screen = upsertScreen({
        pc_name: pcName,
        image: payload.image,
        student_name: payload.student_name || null,
      });
      if (screen) {
        io.to('admins').emit('screen:update', screen);
      }
    });

    socket.on('client:screen-stop', (payload = {}) => {
      const pcName = normalizePcName(payload.pc_name || socket.data.pc_name);
      if (!pcName) return;
      if (removeScreen(pcName)) {
        io.to('admins').emit('screen:remove', { pc_name: pcName });
      }
      updatePresence({ ...payload, pc_name: pcName, student_name: null }, 'socket-screen-stop');
    });

    // ── Chat: Client reply to admin ────────────────────────────
    socket.on('chat:reply-to-admin', (data = {}) => {
      const pcName = normalizePcName(socket.data.pc_name);
      if (!pcName || !data.message) return;

      const payload = {
        id: `msg_${Date.now()}_${Math.random().toString(36).substr(2, 6)}`,
        pc_name: pcName,
        student_name: data.student_name || socket.data.student_name || null,
        message: data.message,
        timestamp: data.timestamp || new Date().toISOString(),
      };

      // Forward to all admins
      io.to('admins').emit('chat:message-from-client', payload);

      // Save to Firebase (async)
      saveChatMessage({ ...payload, type: 'client_reply' }).catch(err => {
        console.error('[CHAT] Failed to save client reply:', err.message);
      });
    });

    // ── Client acknowledgement for attention mode ──────────────
    socket.on('client:attention-ack', (payload = {}) => {
      const pcName = normalizePcName(socket.data.pc_name);
      if (!pcName) return;
      
      io.to('admins').emit('client:attention-ack', {
        pc_name: pcName,
        acknowledged: true,
        timestamp: Date.now(),
      });
    });

    // ── Activity Monitoring ────────────────────────────────────
    socket.on('client:activity', async (activity = {}) => {
      const pcName = normalizePcName(socket.data.pc_name);
      if (!pcName) return;

      // Broadcast to admin dashboard for live feed
      io.to('admins').emit('activity:new', {
        ...activity,
        pc_name: pcName,
        received_at: Date.now(),
      });

      // Save to database (async, don't block)
      saveActivityToDatabase(activity).catch(err => {
        console.error('[ACTIVITY] Failed to save:', err.message);
      });
    });

    socket.on('disconnect', () => {
      const pcName = normalizePcName(socket.data.pc_name);
      if (!pcName) return;

      markClientDisconnected(pcName, socket.id);
      if (removeScreen(pcName)) {
        io.to('admins').emit('screen:remove', { pc_name: pcName });
      }
      io.to('admins').emit('presence:update', {
        pc_name: pcName,
        is_online: false,
        last_seen: Date.now(),
      });
    });
  });

  return io;
}

// ── Helper: Save chat message to database (Firebase) ───────────
async function saveChatMessage(messageData) {
  if (firebaseService.chat && firebaseService.chat.create) {
    await firebaseService.chat.create(messageData);
  }
}

// ── Helper: Save activity to database (Firebase) ───────────────
async function saveActivityToDatabase(activity) {
  await firebaseService.activities.create(activity);
}

module.exports = { attachRealtimeHub };
