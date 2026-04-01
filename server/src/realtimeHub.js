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
      origin: true,
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

module.exports = { attachRealtimeHub };
