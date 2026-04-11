const express = require('express');
const router = express.Router();
const activitiesController = require('../controllers/activitiesController');
const { requireAdmin } = require('../middleware/requireAdmin');

/**
 * Activity Monitoring Routes
 */

// Create activity log (client Electron mengirim data — tidak perlu admin auth)
router.post('/', activitiesController.createActivity);

// Semua endpoint GET & DELETE memerlukan admin authentication
router.get('/',                    requireAdmin, activitiesController.getActivities);
router.get('/summary',             requireAdmin, activitiesController.getActivitySummary);
router.get('/session/:sessionId',  requireAdmin, activitiesController.getSessionActivities);
router.get('/student/:studentId',  requireAdmin, activitiesController.getStudentActivities);
router.get('/stats',               requireAdmin, activitiesController.getActivityStats);
router.get('/top-sites',           requireAdmin, activitiesController.getTopSites);
router.get('/top-apps',            requireAdmin, activitiesController.getTopApps);
router.delete('/cleanup',          requireAdmin, activitiesController.cleanupOldActivities);

module.exports = router;
