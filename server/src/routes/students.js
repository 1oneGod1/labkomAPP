const express = require('express');
const router  = express.Router();
const { getStudents, createStudent, updateStudent, deleteStudent } = require('../controllers/studentsController');
const { requireAdmin } = require('../middleware/requireAdmin');

router.get('/',      requireAdmin, getStudents);
router.post('/',     requireAdmin, createStudent);
router.put('/:id',   requireAdmin, updateStudent);
router.delete('/:id', requireAdmin, deleteStudent);

module.exports = router;
