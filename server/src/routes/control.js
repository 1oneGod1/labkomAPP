const express = require('express');
const router  = express.Router();
const { getSettings, updateSettings } = require('../controllers/controlController');
const { requireAdmin } = require('../middleware/requireAdmin');

router.get('/settings',  requireAdmin, getSettings);
router.post('/settings', requireAdmin, updateSettings);

module.exports = router;
