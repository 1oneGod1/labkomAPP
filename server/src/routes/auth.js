const express = require('express');
const router  = express.Router();
const { login, logout, forceLogout, checkStatus } = require('../controllers/authController');

router.post('/login',        login);
router.post('/logout',       logout);
router.post('/force-logout', forceLogout);   // untuk guru/admin & cleanup
router.get('/status/:nis',   checkStatus);   // cek real-time status login siswa

module.exports = router;
