const express = require('express');
const router  = express.Router();
const { submitCheck, getChecks, getChecksSummary } = require('../controllers/checksController');
const { requireAdmin }  = require('../middleware/requireAdmin');
const { requireClient } = require('../middleware/requireClient');

// POST /api/checks -> kirim hasil checklist dari klien (butuh client token)
router.post('/', requireClient, submitCheck);

// GET /api/checks -> ambil log checklist (admin)
router.get('/', requireAdmin, getChecks);

// GET /api/checks/summary -> ringkasan per PC (admin)
router.get('/summary', requireAdmin, getChecksSummary);

module.exports = router;
