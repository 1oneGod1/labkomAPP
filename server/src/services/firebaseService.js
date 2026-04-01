// ─── Firebase Service Layer ─────────────────────────────────────────────────
// Abstraction layer untuk operasi Firestore
// Menyediakan fungsi-fungsi CRUD yang mudah digunakan

const { db, admin } = require('../config/firebase');

// ═══════════════════════════════════════════════════════════════════════════
// HELPER FUNCTIONS
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Check apakah Firestore sudah terinisialisasi
 */
function isFirestoreAvailable() {
  return db !== null;
}

/**
 * Generate timestamp untuk Firestore
 */
function timestamp() {
  return admin.firestore.Timestamp.now();
}

/**
 * Convert Firestore document ke plain object
 */
function docToObject(doc) {
  if (!doc.exists) return null;
  return { id: doc.id, ...doc.data() };
}

// ═══════════════════════════════════════════════════════════════════════════
// STUDENTS COLLECTION
// ═══════════════════════════════════════════════════════════════════════════

const studentsService = {
  /**
   * Get all students (menggunakan field names yang compatible dengan MySQL)
   */
  async getAll() {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const snapshot = await db.collection('students').orderBy('nis').get();
    return snapshot.docs.map(docToObject);
  },

  /**
   * Get student by NIS
   */
  async getByNis(nis) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const snapshot = await db.collection('students')
      .where('nis', '==', nis)
      .limit(1)
      .get();
    
    if (snapshot.empty) return null;
    return docToObject(snapshot.docs[0]);
  },

  /**
   * Get student by ID
   */
  async getById(id) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const doc = await db.collection('students').doc(id).get();
    return docToObject(doc);
  },

  /**
   * Create new student
   */
  async create(studentData) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    // Check if NIS already exists
    const existing = await this.getByNis(studentData.nis);
    if (existing) {
      throw new Error('NIS sudah terdaftar');
    }

    const data = {
      nis: studentData.nis,
      nama_lengkap: studentData.nama_lengkap,
      kelas: studentData.kelas || null,
      password_hash: studentData.password_hash, // sudah di-hash dari controller
      is_active: studentData.is_active !== undefined ? studentData.is_active : 1,
      created_at: timestamp(),
      updated_at: timestamp(),
    };

    const docRef = await db.collection('students').add(data);
    const result = { id: docRef.id, ...data };
    delete result.password_hash; // Don't return password hash
    return result;
  },

  /**
   * Update student
   */
  async update(id, updateData) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const data = {
      ...updateData,
      updated_at: timestamp(),
    };

    await db.collection('students').doc(id).update(data);
    const result = await this.getById(id);
    if (result && result.password_hash) {
      delete result.password_hash; // Don't return password hash
    }
    return result;
  },

  /**
   * Delete student (soft delete - set is_active to 0)
   */
  async delete(id) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    await db.collection('students').doc(id).update({
      is_active: 0,
      updated_at: timestamp(),
    });
    return { success: true };
  },

  /**
   * Hard delete student (for testing/admin purposes only)
   */
  async hardDelete(id) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    await db.collection('students').doc(id).delete();
    return { success: true };
  },

  /**
   * Search students by name or NIS
   */
  async search(query) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    // Firestore doesn't support full-text search natively
    // We'll fetch all and filter in-memory for simplicity
    const all = await this.getAll();
    const lowerQuery = query.toLowerCase();
    
    return all.filter(student => 
      student.nis.toLowerCase().includes(lowerQuery) ||
      student.nama_lengkap.toLowerCase().includes(lowerQuery)
    );
  },
};

// ═══════════════════════════════════════════════════════════════════════════
// LAB COMPUTERS COLLECTION
// ═══════════════════════════════════════════════════════════════════════════

const computersService = {
  /**
   * Get all computers
   */
  async getAll() {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const snapshot = await db.collection('lab_computers').orderBy('name').get();
    return snapshot.docs.map(docToObject);
  },

  /**
   * Get computer by ID
   */
  async getById(id) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const doc = await db.collection('lab_computers').doc(id).get();
    return docToObject(doc);
  },

  /**
   * Get computer by name
   */
  async getByName(name) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const snapshot = await db.collection('lab_computers')
      .where('name', '==', name)
      .limit(1)
      .get();
    
    if (snapshot.empty) return null;
    return docToObject(snapshot.docs[0]);
  },

  /**
   * Create new computer
   */
  async create(computerData) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const data = {
      name: computerData.name,
      location: computerData.location || '',
      status: computerData.status || 'active',
      created_at: timestamp(),
      updated_at: timestamp(),
    };

    const docRef = await db.collection('lab_computers').add(data);
    return { id: docRef.id, ...data };
  },

  /**
   * Update computer
   */
  async update(id, updateData) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const data = {
      ...updateData,
      updated_at: timestamp(),
    };

    await db.collection('lab_computers').doc(id).update(data);
    return this.getById(id);
  },

  /**
   * Delete computer
   */
  async delete(id) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    await db.collection('lab_computers').doc(id).delete();
    return { success: true };
  },
};

// ═══════════════════════════════════════════════════════════════════════════
// SESSIONS COLLECTION
// ═══════════════════════════════════════════════════════════════════════════

const sessionsService = {
  /**
   * Get all sessions
   */
  async getAll(limit = 100) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const snapshot = await db.collection('sessions')
      .orderBy('login_time', 'desc')
      .limit(limit)
      .get();
    
    return snapshot.docs.map(docToObject);
  },

  /**
   * Get session by ID
   */
  async getById(id) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const doc = await db.collection('sessions').doc(id).get();
    return docToObject(doc);
  },

  /**
   * Get active sessions
   */
  async getActive() {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const snapshot = await db.collection('sessions')
      .where('status', '==', 'active')
      .orderBy('login_time', 'desc')
      .get();
    
    return snapshot.docs.map(docToObject);
  },

  /**
   * Get sessions by student NIM
   */
  async getByStudent(studentId) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const snapshot = await db.collection('sessions')
      .where('student_id', '==', studentId)
      .orderBy('login_time', 'desc')
      .limit(50)
      .get();
    
    return snapshot.docs.map(docToObject);
  },

  /**
   * Create new session (login)
   */
  async create(sessionData) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const data = {
      student_id: sessionData.student_id,
      computer_id: sessionData.computer_id,
      computer_name: sessionData.computer_name,
      login_time: timestamp(),
      logout_time: null,
      duration_minutes: null,
      status: 'active',
      created_at: timestamp(),
      updated_at: timestamp(),
    };

    const docRef = await db.collection('sessions').add(data);
    return { id: docRef.id, ...data };
  },

  /**
   * End session (logout)
   */
  async endSession(id) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const session = await this.getById(id);
    if (!session) throw new Error('Session not found');

    const logoutTime = timestamp();
    const loginTime = session.login_time;
    
    // Calculate duration in minutes
    const durationMs = logoutTime.toMillis() - loginTime.toMillis();
    const durationMinutes = Math.floor(durationMs / 1000 / 60);

    const data = {
      logout_time: logoutTime,
      duration_minutes: durationMinutes,
      status: 'completed',
      updated_at: logoutTime,
    };

    await db.collection('sessions').doc(id).update(data);
    return this.getById(id);
  },

  /**
   * Get session history with date range
   */
  async getHistory(startDate, endDate, limit = 100) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    let query = db.collection('sessions')
      .where('status', '==', 'completed')
      .orderBy('login_time', 'desc');

    if (startDate) {
      query = query.where('login_time', '>=', admin.firestore.Timestamp.fromDate(new Date(startDate)));
    }
    if (endDate) {
      query = query.where('login_time', '<=', admin.firestore.Timestamp.fromDate(new Date(endDate)));
    }

    const snapshot = await query.limit(limit).get();
    return snapshot.docs.map(docToObject);
  },
};

// ═══════════════════════════════════════════════════════════════════════════
// FACILITY CHECKS COLLECTION
// ═══════════════════════════════════════════════════════════════════════════

const checksService = {
  /**
   * Create facility check
   */
  async create(checkData) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const data = {
      session_id: checkData.session_id,
      student_id: checkData.student_id,
      computer_id: checkData.computer_id,
      computer_name: checkData.computer_name,
      mouse: checkData.mouse === true || checkData.mouse === 1,
      keyboard: checkData.keyboard === true || checkData.keyboard === 1,
      monitor: checkData.monitor === true || checkData.monitor === 1,
      headset: checkData.headset === true || checkData.headset === 1,
      checked_at: timestamp(),
      created_at: timestamp(),
      updated_at: timestamp(),
    };

    const docRef = await db.collection('facility_checks').add(data);
    return { id: docRef.id, ...data };
  },

  /**
   * Get checks by session
   */
  async getBySession(sessionId) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const snapshot = await db.collection('facility_checks')
      .where('session_id', '==', sessionId)
      .orderBy('checked_at', 'desc')
      .get();
    
    return snapshot.docs.map(docToObject);
  },

  /**
   * Get all checks with limit
   */
  async getAll(limit = 100) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const snapshot = await db.collection('facility_checks')
      .orderBy('checked_at', 'desc')
      .limit(limit)
      .get();
    
    return snapshot.docs.map(docToObject);
  },
};

// ═══════════════════════════════════════════════════════════════════════════
// CONTROL SETTINGS (Singleton)
// ═══════════════════════════════════════════════════════════════════════════

const controlService = {
  /**
   * Get control settings
   */
  async get() {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const doc = await db.collection('control_settings').doc('global').get();
    if (!doc.exists) {
      // Create default settings
      return this.set({
        lock_enabled: false,
        client_locked: false,
      });
    }
    return docToObject(doc);
  },

  /**
   * Set control settings
   */
  async set(settings) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const data = {
      ...settings,
      updated_at: timestamp(),
      updated_by: 'admin',
    };

    await db.collection('control_settings').doc('global').set(data, { merge: true });
    return this.get();
  },

  /**
   * Update specific setting
   */
  async update(key, value) {
    if (!isFirestoreAvailable()) throw new Error('Firestore not available');
    
    const data = {
      [key]: value,
      updated_at: timestamp(),
      updated_by: 'admin',
    };

    await db.collection('control_settings').doc('global').update(data);
    return this.get();
  },
};

// ═══════════════════════════════════════════════════════════════════════════
// EXPORTS
// ═══════════════════════════════════════════════════════════════════════════

module.exports = {
  isFirestoreAvailable,
  timestamp,
  students: studentsService,
  computers: computersService,
  sessions: sessionsService,
  checks: checksService,
  control: controlService,
};
