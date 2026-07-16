const { Server } = require('socket.io');
const { validateToken } = require('./services/adminSessionService');
const clientTokenService = require('./services/clientTokenService');
const {
  normalizePcName,
  upsertClient,
  markClientDisconnected,
  getClientRegistry,
} = require('./services/clientRegistryService');
const {
  upsertScreen,
  toLegacyScreen,
  removeScreen,
  getActiveScreens,
} = require('./services/screenRelayService');
const firebaseService = require('./services/firebaseService');
const { normalizeShowTargets, validateShowFrame } = require('./services/screenShareProtocol');
const { normalizeDisplayId, validateStudentScreenFrame } = require('./services/studentScreenProtocol');

const screenWatchers = new Map();
let activeAdminShow = null;

function isShowRecipient(clientSocket, targetSet) {
  if (clientSocket.data.role !== 'client') return false;
  const pcName = normalizePcName(clientSocket.data.pc_name || clientSocket.data.claimed_pc_name);
  return Boolean(pcName && (!targetSet || targetSet.has(pcName)));
}

function getShowRecipients(io, targetSet) {
  const recipients = [];
  for (const [, candidate] of io.sockets.sockets) {
    if (isShowRecipient(candidate, targetSet)) recipients.push(candidate);
  }
  return recipients;
}

function emitShowEvent(io, targetSet, eventName, payload, { volatile = false } = {}) {
  const recipients = getShowRecipients(io, targetSet);
  for (const recipient of recipients) {
    if (volatile) recipient.volatile.emit(eventName, payload);
    else recipient.emit(eventName, payload);
  }
  return recipients.length;
}

function buildShowMetadata(show) {
  return {
    session_id: show.session_id,
    started_at: show.started_at,
    source_label: show.source_label,
    targets: show.targets ? Array.from(show.targets) : 'all',
    profile: show.profile,
    paused: Boolean(show.paused),
  };
}

function startActiveShowForClient(socket) {
  if (!activeAdminShow || !isShowRecipient(socket, activeAdminShow.targets)) return;
  socket.emit('admin:screen-share-start', buildShowMetadata(activeAdminShow));
}

function getClientRoom(pcName) {
  const normalizedPcName = normalizePcName(pcName);
  return normalizedPcName ? `client:${normalizedPcName}` : null;
}

function setScreenWatcher(pcName, socketId, displayId = null) {
  const normalizedPcName = normalizePcName(pcName);
  if (!normalizedPcName || !socketId) return;

  const watchers = screenWatchers.get(normalizedPcName) || new Map();
  watchers.delete(socketId);
  watchers.set(socketId, normalizeDisplayId(displayId));
  screenWatchers.set(normalizedPcName, watchers);
}

function removeScreenWatcher(pcName, socketId) {
  const normalizedPcName = normalizePcName(pcName);
  const watchers = screenWatchers.get(normalizedPcName);
  if (!normalizedPcName || !watchers) return;

  watchers.delete(socketId);
  if (watchers.size === 0) {
    screenWatchers.delete(normalizedPcName);
  }
}

function emitScreenQuality(io, pcName) {
  const normalizedPcName = normalizePcName(pcName);
  const room = getClientRoom(normalizedPcName);
  if (!normalizedPcName || !room) return;

  const watchers = screenWatchers.get(normalizedPcName);
  const selectedDisplays = watchers ? Array.from(watchers.values()) : [];
  io.to(room).emit('screen:quality', {
    pc_name: normalizedPcName,
    mode: selectedDisplays.length > 0 ? 'focus' : 'overview',
    display_id: selectedDisplays.at(-1) || null,
  });
}

function emitStudentScreenToAdmins(io, screen) {
  if (!screen) return 0;

  const legacyScreen = toLegacyScreen(screen);
  const binaryPacket = screen.frame?.length ? {
    pc_name: screen.pc_name,
    student_name: screen.student_name,
    frame: screen.frame,
    mime: screen.mime,
    width: screen.width,
    height: screen.height,
    sequence: screen.sequence,
    captured_at: screen.captured_at,
    display_id: screen.display_id,
    display_label: screen.display_label,
    monitors: screen.monitors,
    received_at: screen.updated_at,
  } : null;

  let count = 0;
  for (const [, candidate] of io.sockets.sockets) {
    if (candidate.data.role !== 'admin') continue;
    if (binaryPacket && candidate.data.capabilities?.student_screen_binary_v1) {
      candidate.volatile.emit('screen:update-v2', binaryPacket);
    } else if (legacyScreen) {
      candidate.volatile.emit('screen:update', legacyScreen);
    }
    count += 1;
  }
  return count;
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
      socket.data.capabilities = {
        student_screen_binary_v1: Boolean(socket.handshake.auth?.capabilities?.student_screen_binary_v1),
      };
      return next();
    }

    // Client role: butuh device token yang sudah teregister
    const clientToken = socket.handshake.auth?.client_token;
    const claim = clientTokenService.validateToken(clientToken);
    if (!claim) {
      return next(new Error('unauthorized client — token invalid atau expired'));
    }
    socket.data.role = 'client';
    socket.data.device_id = claim.device_id;
    socket.data.claimed_pc_name = claim.pc_name;
    socket.data.pc_name = claim.pc_name;
    return next();
  });

  io.on('connection', (socket) => {
    if (socket.data.role === 'admin') {
      socket.join('admins');
      socket.emit('screens:snapshot', getActiveScreens());
      socket.emit('presence:snapshot', getClientRegistry().filter((entry) => entry.is_online));
      socket.on('admin:presence-snapshot-request', () => {
        socket.emit('presence:snapshot', getClientRegistry().filter((entry) => entry.is_online));
      });

      const setWatchTarget = (nextPcName = null, nextDisplayId = null) => {
        const previousPcName = normalizePcName(socket.data.watch_pc_name);
        const normalizedNextPcName = normalizePcName(nextPcName);
        const normalizedNextDisplayId = normalizeDisplayId(nextDisplayId);
        const previousDisplayId = normalizeDisplayId(socket.data.watch_display_id);
        if (previousPcName === normalizedNextPcName && previousDisplayId === normalizedNextDisplayId) return;

        const affectedPcs = new Set();

        if (previousPcName) {
          removeScreenWatcher(previousPcName, socket.id);
          affectedPcs.add(previousPcName);
        }

        socket.data.watch_pc_name = normalizedNextPcName || null;
        socket.data.watch_display_id = normalizedNextDisplayId;

        if (normalizedNextPcName) {
          setScreenWatcher(normalizedNextPcName, socket.id, normalizedNextDisplayId);
          affectedPcs.add(normalizedNextPcName);
        }

        for (const pcName of affectedPcs) emitScreenQuality(io, pcName);
      };

      socket.on('admin:watch-screen', ({ pc_name, display_id } = {}) => {
        setWatchTarget(pc_name || null, display_id || null);
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

      // ── Admin Screen Share ─────────────────────────────────────
      socket.on('admin:screen-share-start', (data = {}, callback) => {
        if (activeAdminShow && activeAdminShow.owner_socket_id !== socket.id) {
          callback?.({ success: false, status: 409, message: 'Admin lain sedang berbagi layar.' });
          return;
        }

        const targets = normalizeShowTargets(data.targets, normalizePcName);
        if (targets && targets.size === 0) {
          callback?.({ success: false, status: 400, message: 'Pilih minimal satu PC siswa.' });
          return;
        }

        activeAdminShow = {
          owner_socket_id: socket.id,
          session_id: `show_${Date.now()}_${socket.id.slice(0, 6)}`,
          targets,
          started_at: Date.now(),
          source_label: String(data.source_label || 'Layar Instruktur').slice(0, 120),
          profile: String(data.profile || 'balanced').slice(0, 24),
          paused: false,
        };
        socket.data.admin_screen_share_active = true;

        const metadata = buildShowMetadata(activeAdminShow);
        const count = emitShowEvent(io, targets, 'admin:screen-share-start', metadata);
        callback?.({ success: true, count, session_id: activeAdminShow.session_id });
      });

      // Kompatibilitas Admin lama yang masih mengirim data-URI base64.
      socket.on('admin:screen-share-frame', (data = {}) => {
        if (!data.image || !activeAdminShow || activeAdminShow.owner_socket_id !== socket.id || activeAdminShow.paused) return;
        emitShowEvent(io, activeAdminShow.targets, 'admin:screen-share-frame', {
          image: data.image,
          session_id: activeAdminShow.session_id,
        }, { volatile: true });
      });

      // Transport v2: frame biner, backpressure melalui acknowledgement, dan fallback client lama.
      socket.on('admin:screen-share-frame-v2', (data = {}, callback) => {
        if (!activeAdminShow || activeAdminShow.owner_socket_id !== socket.id) {
          callback?.({ success: false, status: 409, message: 'Sesi berbagi belum aktif.' });
          return;
        }
        if (activeAdminShow.paused) {
          callback?.({ success: true, count: 0, paused: true, received_at: Date.now() });
          return;
        }

        const validated = validateShowFrame(data);
        if (!validated.ok) {
          callback?.({ success: false, status: validated.status, message: validated.message });
          return;
        }

        const packet = {
          ...validated.packet,
          session_id: activeAdminShow.session_id,
        };
        const { frame, mime } = packet;
        const recipients = getShowRecipients(io, activeAdminShow.targets);
        let fallbackImage = null;

        for (const recipient of recipients) {
          if (recipient.data.capabilities?.admin_screen_binary_v1) {
            recipient.volatile.emit('admin:screen-share-frame-v2', packet);
          } else {
            fallbackImage ||= `data:${mime};base64,${frame.toString('base64')}`;
            recipient.volatile.emit('admin:screen-share-frame', {
              image: fallbackImage,
              session_id: activeAdminShow.session_id,
            });
          }
        }

        callback?.({ success: true, count: recipients.length, received_at: Date.now() });
      });

      socket.on('admin:screen-share-pause', ({ paused } = {}, callback) => {
        if (!activeAdminShow || activeAdminShow.owner_socket_id !== socket.id) {
          callback?.({ success: false, status: 409, message: 'Sesi berbagi belum aktif.' });
          return;
        }
        activeAdminShow.paused = Boolean(paused);
        const count = emitShowEvent(io, activeAdminShow.targets, 'admin:screen-share-pause', {
          session_id: activeAdminShow.session_id,
          paused: activeAdminShow.paused,
        });
        callback?.({ success: true, count, paused: activeAdminShow.paused });
      });

      socket.on('admin:screen-share-stop', (callback) => {
        if (!activeAdminShow || activeAdminShow.owner_socket_id !== socket.id) {
          callback?.({ success: true, count: 0 });
          return;
        }
        const count = emitShowEvent(io, activeAdminShow.targets, 'admin:screen-share-stop', {
          session_id: activeAdminShow.session_id,
        });
        activeAdminShow = null;
        socket.data.admin_screen_share_active = false;
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
        if (activeAdminShow?.owner_socket_id === socket.id) {
          emitShowEvent(io, activeAdminShow.targets, 'admin:screen-share-stop', {
            session_id: activeAdminShow.session_id,
          });
          activeAdminShow = null;
        }
      });

      return;
    }

    socket.join('clients');

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
      if (payload.capabilities && typeof payload.capabilities === 'object') {
        socket.data.capabilities = {
          admin_screen_binary_v1: Boolean(payload.capabilities.admin_screen_binary_v1),
          student_screen_binary_v1: Boolean(payload.capabilities.student_screen_binary_v1),
          multi_monitor_v1: Boolean(payload.capabilities.multi_monitor_v1),
        };
      }

      const entry = upsertClient({
        pc_name: socket.data.claimed_pc_name,
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

    socket.emit('server:capabilities', {
      student_screen_binary_v1: true,
      multi_monitor_v1: true,
    });
    bindClientRoom(socket.data.claimed_pc_name);
    startActiveShowForClient(socket);

    socket.on('client:hello', (payload = {}) => {
      updatePresence(payload, 'socket-hello');
    });

    socket.on('client:heartbeat', (payload = {}) => {
      updatePresence(payload, 'socket-heartbeat');
    });

    socket.on('client:screen', (payload = {}) => {
      const pcName = normalizePcName(socket.data.claimed_pc_name);
      if (!pcName || !payload.image) return;

      updatePresence({ ...payload, pc_name: pcName }, 'socket-screen');
      const screen = upsertScreen({
        pc_name: pcName,
        image: payload.image,
        student_name: payload.student_name || null,
      });
      if (screen) emitStudentScreenToAdmins(io, screen);
    });

    socket.on('client:screen-v2', (payload = {}, callback) => {
      const pcName = normalizePcName(socket.data.claimed_pc_name);
      if (!pcName) {
        callback?.({ success: false, status: 401, message: 'Identitas PC tidak valid.' });
        return;
      }

      const validated = validateStudentScreenFrame(payload);
      if (!validated.ok) {
        callback?.({ success: false, status: validated.status, message: validated.message });
        return;
      }

      updatePresence({ student_name: validated.packet.student_name }, 'socket-screen-v2');
      const screen = upsertScreen({
        pc_name: pcName,
        ...validated.packet,
      });
      const count = emitStudentScreenToAdmins(io, screen);
      callback?.({
        success: true,
        count,
        sequence: validated.packet.sequence,
        received_at: Date.now(),
      });
    });

    socket.on('client:screen-stop', (payload = {}) => {
      const pcName = normalizePcName(socket.data.claimed_pc_name);
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
      saveActivityToDatabase({ ...activity, pc_name: pcName }).catch(err => {
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
