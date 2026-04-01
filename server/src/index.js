require('dotenv').config();
const express = require('express');
const cors    = require('cors');
const os      = require('os');
const http    = require('http');

function getLanIp() {
  const ifaces = os.networkInterfaces();
  for (const name of Object.keys(ifaces)) {
    for (const iface of ifaces[name]) {
      if (iface.family === 'IPv4' && !iface.internal) return iface.address;
    }
  }
  return '127.0.0.1';
}

const authRoutes       = require('./routes/auth');
const sessionRoutes    = require('./routes/sessions');
const adminRoutes      = require('./routes/admin');
const monitoringRoutes = require('./routes/monitoring');
const studentsRoutes   = require('./routes/students');
const historyRoutes    = require('./routes/history');
const controlRoutes    = require('./routes/control');
const checksRoutes     = require('./routes/checks');
const screensRoutes    = require('./routes/screens');
const clientCmdRoutes  = require('./routes/clientcmd');
const { attachRealtimeHub } = require('./realtimeHub');

const app  = express();
const PORT = process.env.PORT || 3001;
const server = http.createServer(app);

// =====================
// Middleware
// =====================
// Izinkan semua origin termasuk null (Electron file:// protocol)
app.use(cors({
  origin: (origin, callback) => callback(null, true),
  credentials: true,
}));
app.use(express.json({ limit: '2mb' }));          // screenshots butuh ruang lebih
app.use(express.urlencoded({ extended: true, limit: '2mb' }));

// Request logger sederhana
app.use((req, _res, next) => {
  const now = new Date().toLocaleTimeString('id-ID');
  console.log(`[${now}] ${req.method} ${req.url}`);
  next();
});

// =====================
// Routes
// =====================
app.get('/', (_req, res) => {
  res.json({ message: 'Labkom Server berjalan!', version: '1.0.0' });
});

app.use('/api/auth',       authRoutes);
app.use('/api/sessions',   sessionRoutes);
app.use('/api/admin',      adminRoutes);
app.use('/api/monitoring', monitoringRoutes);
app.use('/api/students',   studentsRoutes);
app.use('/api/history',    historyRoutes);
app.use('/api/control',    controlRoutes);
app.use('/api/checks',     checksRoutes);
app.use('/api/screens',    screensRoutes);
app.use('/api/client-cmd', clientCmdRoutes);

// 404 handler
app.use((_req, res) => {
  res.status(404).json({ success: false, message: 'Endpoint tidak ditemukan.' });
});

// =====================
attachRealtimeHub(server);

// Start Server
// =====================
server.listen(PORT, '0.0.0.0', () => {
  const lanIp = getLanIp();
  console.log('========================================');
  console.log(`  LABKOM SERVER berjalan di port ${PORT}`);
  console.log(`  Lokal : http://localhost:${PORT}`);
  console.log(`  LAN   : http://${lanIp}:${PORT}`);
  console.log('========================================');
});
