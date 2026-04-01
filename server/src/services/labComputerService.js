const db = require('../config/database');
const { normalizePcName, normalizeMac } = require('./clientRegistryService');

async function getLabComputers() {
  const [rows] = await db.query(`
    SELECT
      id,
      pc_name,
      label,
      bound_hostname,
      bound_mac,
      last_known_ip,
      mapped_at
    FROM lab_computers
    ORDER BY pc_name
  `);

  return rows.map((row) => ({
    ...row,
    pc_name: normalizePcName(row.pc_name),
    bound_hostname: normalizePcName(row.bound_hostname) || null,
    bound_mac: normalizeMac(row.bound_mac) || null,
  }));
}

async function getLabComputerByPcName(pcName) {
  const normalizedPcName = normalizePcName(pcName);
  if (!normalizedPcName) return null;

  const [rows] = await db.query(`
    SELECT
      id,
      pc_name,
      label,
      bound_hostname,
      bound_mac,
      last_known_ip,
      mapped_at
    FROM lab_computers
    WHERE pc_name = ?
    LIMIT 1
  `, [normalizedPcName]);

  if (!rows.length) return null;
  const [row] = rows;
  return {
    ...row,
    pc_name: normalizePcName(row.pc_name),
    bound_hostname: normalizePcName(row.bound_hostname) || null,
    bound_mac: normalizeMac(row.bound_mac) || null,
  };
}

async function resolveMappedLabPc({ pc_name, mac } = {}) {
  const normalizedPcName = normalizePcName(pc_name);
  const normalizedMac = normalizeMac(mac);

  if (normalizedPcName) {
    const [rows] = await db.query(`
      SELECT pc_name, label, bound_hostname, bound_mac
      FROM lab_computers
      WHERE UPPER(bound_hostname) = ?
      LIMIT 1
    `, [normalizedPcName]);

    if (rows.length) {
      return {
        ...rows[0],
        pc_name: normalizePcName(rows[0].pc_name),
        bound_hostname: normalizePcName(rows[0].bound_hostname) || null,
        bound_mac: normalizeMac(rows[0].bound_mac) || null,
      };
    }
  }

  if (normalizedMac) {
    const [rows] = await db.query(`
      SELECT pc_name, label, bound_hostname, bound_mac
      FROM lab_computers
      WHERE UPPER(bound_mac) = ?
      LIMIT 1
    `, [normalizedMac]);

    if (rows.length) {
      return {
        ...rows[0],
        pc_name: normalizePcName(rows[0].pc_name),
        bound_hostname: normalizePcName(rows[0].bound_hostname) || null,
        bound_mac: normalizeMac(rows[0].bound_mac) || null,
      };
    }
  }

  return null;
}

async function assignDeviceToLabComputer({ target_pc_name, source_pc_name, source_mac, source_ip } = {}) {
  const targetPcName = normalizePcName(target_pc_name);
  const sourcePcName = normalizePcName(source_pc_name);
  const sourceMac = normalizeMac(source_mac);
  const sourceIp = String(source_ip || '').trim() || null;

  if (!targetPcName) {
    throw new Error('target_pc_name wajib diisi.');
  }
  if (!sourcePcName && !sourceMac) {
    throw new Error('source_pc_name atau source_mac wajib diisi.');
  }

  const conn = await db.getConnection();
  try {
    await conn.beginTransaction();

    const [targetRows] = await conn.query(
      'SELECT pc_name FROM lab_computers WHERE pc_name = ? LIMIT 1',
      [targetPcName]
    );
    if (!targetRows.length) {
      throw new Error('PC tujuan tidak ditemukan.');
    }

    const clearClauses = [];
    const clearParams = [];
    if (sourcePcName) {
      clearClauses.push('UPPER(bound_hostname) = ?');
      clearParams.push(sourcePcName);
    }
    if (sourceMac) {
      clearClauses.push('UPPER(bound_mac) = ?');
      clearParams.push(sourceMac);
    }

    if (clearClauses.length) {
      await conn.query(
        `UPDATE lab_computers
         SET bound_hostname = NULL, bound_mac = NULL, last_known_ip = NULL, mapped_at = NULL
         WHERE pc_name <> ? AND (${clearClauses.join(' OR ')})`,
        [targetPcName, ...clearParams]
      );
    }

    await conn.query(
      `UPDATE lab_computers
       SET bound_hostname = ?, bound_mac = ?, last_known_ip = ?, mapped_at = NOW()
       WHERE pc_name = ?`,
      [sourcePcName || null, sourceMac || null, sourceIp, targetPcName]
    );

    await conn.commit();
  } catch (err) {
    await conn.rollback();
    throw err;
  } finally {
    conn.release();
  }

  return getLabComputerByPcName(targetPcName);
}

async function clearDeviceMapping(target_pc_name) {
  const targetPcName = normalizePcName(target_pc_name);
  if (!targetPcName) {
    throw new Error('target_pc_name wajib diisi.');
  }

  await db.query(
    `UPDATE lab_computers
     SET bound_hostname = NULL, bound_mac = NULL, last_known_ip = NULL, mapped_at = NULL
     WHERE pc_name = ?`,
    [targetPcName]
  );

  return getLabComputerByPcName(targetPcName);
}

module.exports = {
  getLabComputers,
  getLabComputerByPcName,
  resolveMappedLabPc,
  assignDeviceToLabComputer,
  clearDeviceMapping,
};
