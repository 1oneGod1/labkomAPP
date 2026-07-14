const express = require('express');
const router  = express.Router();
const { verifyPassword, login, me, logout, refreshToken, listDeviceClaims, revokeDeviceClaim } = require('../controllers/adminController');
const { requireAdmin } = require('../middleware/requireAdmin');

// POST /api/admin/verify-password
router.post('/verify-password', verifyPassword);
router.post('/login', login);
router.get('/me', me);
router.post('/refresh', requireAdmin, refreshToken);
router.post('/logout', requireAdmin, logout);

// Device claim management (admin only)
router.get('/device-claims',           requireAdmin, listDeviceClaims);
router.post('/device-claims/revoke',   requireAdmin, revokeDeviceClaim);

module.exports = router;
