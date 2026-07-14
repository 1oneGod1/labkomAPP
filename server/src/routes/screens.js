/**
 * /api/screens - HTTP fallback screenshot relay
 * Client PC -> POST /api/screens
 * Admin     -> GET  /api/screens (protected)
 */
const express = require('express');
const router  = express.Router();
const { requireAdmin }  = require('../middleware/requireAdmin');
const { requireClient } = require('../middleware/requireClient');
const { normalizePcName } = require('../services/clientRegistryService');
const { upsertScreen, getActiveScreens, removeScreen } = require('../services/screenRelayService');

router.post('/', requireClient, (req, res) => {
  const { pc_name, image, student_name } = req.body;
  // Client hanya boleh kirim untuk pc_name miliknya sendiri (sesuai claim token)
  if (req.actor?.role === 'client' && normalizePcName(pc_name) !== req.actor.pc_name) {
    return res.status(403).json({ success: false, message: 'pc_name tidak sesuai claim device.' });
  }
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

router.delete('/:pc_name', requireClient, (req, res) => {
  const targetPc = normalizePcName(req.params.pc_name);
  if (req.actor?.role === 'client' && targetPc !== req.actor.pc_name) {
    return res.status(403).json({ success: false, message: 'pc_name tidak sesuai claim device.' });
  }
  removeScreen(targetPc);
  return res.json({ success: true });
});

module.exports = router;
