const express = require('express');
const router  = express.Router();
const { verifyPassword, login, me, logout, refreshToken } = require('../controllers/adminController');
const { requireAdmin } = require('../middleware/requireAdmin');

// POST /api/admin/verify-password
router.post('/verify-password', verifyPassword);
router.post('/login', login);
router.get('/me', me);
router.post('/refresh', requireAdmin, refreshToken);
router.post('/logout', requireAdmin, logout);

module.exports = router;
