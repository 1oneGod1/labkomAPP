const express = require('express');
const router  = express.Router();
const {
  getPcs,
  mapDevice,
  clearMapping,
  forceLogoutPc,
  forceLogoutAll,
} = require('../controllers/monitoringController');
const { requireAdmin } = require('../middleware/requireAdmin');

router.get('/pcs',              requireAdmin, getPcs);
router.post('/map-device',      requireAdmin, mapDevice);
router.post('/clear-mapping',   requireAdmin, clearMapping);
router.post('/force-logout',    requireAdmin, forceLogoutPc);
router.post('/force-logout-all', requireAdmin, forceLogoutAll);

module.exports = router;
