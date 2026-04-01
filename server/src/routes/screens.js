/**
 * /api/screens - HTTP fallback screenshot relay
 * Client PC -> POST /api/screens
 * Admin     -> GET  /api/screens (protected)
 */
const express = require('express');
const router  = express.Router();
const { requireAdmin } = require('../middleware/requireAdmin');
const { normalizePcName } = require('../services/clientRegistryService');
const { upsertScreen, getActiveScreens, removeScreen } = require('../services/screenRelayService');

router.post('/', (req, res) => {
  const { pc_name, image, student_name } = req.body;
  const screen = upsertScreen({ pc_name, image, student_name });
  if (!screen) {
    return res.status(400).json({ success: false, message: 'pc_name dan image wajib diisi.' });
  }
  return res.json({ success: true });
});

router.get('/', requireAdmin, (_req, res) => {
  res.set('Cache-Control', 'no-store');
  return res.json({ success: true, data: getActiveScreens() });
});

router.delete('/:pc_name', (req, res) => {
  removeScreen(normalizePcName(req.params.pc_name));
  return res.json({ success: true });
});

module.exports = router;
