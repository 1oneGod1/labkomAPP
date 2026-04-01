const express = require('express');
const router  = express.Router();
const { getHistory } = require('../controllers/historyController');
const { requireAdmin } = require('../middleware/requireAdmin');

// /api/history?date=YYYY-MM-DD&page=1&limit=50
router.get('/', requireAdmin, getHistory);

module.exports = router;
