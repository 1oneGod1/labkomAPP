const express = require('express');
const router  = express.Router();
const { getStudents, createStudent, updateStudent, deleteStudent, importBatch, downloadTemplate } = require('../controllers/studentsController');
const { requireAdmin } = require('../middleware/requireAdmin');

router.get('/',               requireAdmin, getStudents);
router.post('/',              requireAdmin, createStudent);
router.post('/import-batch',  requireAdmin, importBatch);
router.get('/template',       requireAdmin, downloadTemplate);
router.put('/:id',            requireAdmin, updateStudent);
router.delete('/:id',         requireAdmin, deleteStudent);

module.exports = router;
