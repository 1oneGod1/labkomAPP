const { validateToken } = require('../services/adminSessionService');
const { logAdminAction } = require('../services/adminAuditService');

function requireAdmin(req, res, next) {
  const authHeader = req.headers.authorization || '';
  const match = authHeader.match(/^Bearer\s+(.+)$/i);
  const token = match ? match[1] : null;

  if (!validateToken(token)) {
    logAdminAction(req, {
      action: 'ADMIN_UNAUTHORIZED',
      statusCode: 401,
      success: false,
    }).catch(() => {});
    return res.status(401).json({
      success: false,
      message: 'Akses admin ditolak. Silakan login ulang.',
    });
  }

  req.adminToken = token;
  res.on('finish', () => {
    logAdminAction(req, {
      action: 'ADMIN_ACTION',
      statusCode: res.statusCode,
      success: res.statusCode < 400,
    }).catch(() => {});
  });

  return next();
}

module.exports = { requireAdmin };
