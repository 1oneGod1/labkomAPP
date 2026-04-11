const express = require('express');
const router  = express.Router();
const { login, logout, forceLogout, checkStatus } = require('../controllers/authController');
const { requireAdmin } = require('../middleware/requireAdmin');

router.post('/login',        login);
router.post('/logout',       logout);
router.post('/force-logout', requireAdmin, forceLogout);   // memerlukan admin auth
router.get('/status/:nis',   checkStatus);   // cek real-time status login siswa

module.exports = router;
