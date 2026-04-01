const express = require('express');
const router  = express.Router();
const { getAllSessions, getActiveSessions } = require('../controllers/sessionController');
const { requireAdmin } = require('../middleware/requireAdmin');

router.get('/',       requireAdmin, getAllSessions);
router.get('/active', requireAdmin, getActiveSessions);

module.exports = router;
