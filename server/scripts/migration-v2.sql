-- ============================================================
-- Migration v2 – Admin Dashboard support
-- Jalankan: mysql -u root labkom_db < scripts/migration-v2.sql
-- ============================================================

USE labkom_db;

-- ── Tabel daftar komputer lab ────────────────────────────────────
CREATE TABLE IF NOT EXISTS lab_computers (
  id         INT AUTO_INCREMENT PRIMARY KEY,
  pc_name    VARCHAR(50) UNIQUE NOT NULL,
  label      VARCHAR(50),
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Seed 20 unit PC lab (sesuaikan jumlah jika perlu)
INSERT IGNORE INTO lab_computers (pc_name, label) VALUES
('PC-01','PC Lab 01'),('PC-02','PC Lab 02'),('PC-03','PC Lab 03'),
('PC-04','PC Lab 04'),('PC-05','PC Lab 05'),('PC-06','PC Lab 06'),
('PC-07','PC Lab 07'),('PC-08','PC Lab 08'),('PC-09','PC Lab 09'),
('PC-10','PC Lab 10'),('PC-11','PC Lab 11'),('PC-12','PC Lab 12'),
('PC-13','PC Lab 13'),('PC-14','PC Lab 14'),('PC-15','PC Lab 15'),
('PC-16','PC Lab 16'),('PC-17','PC Lab 17'),('PC-18','PC Lab 18'),
('PC-19','PC Lab 19'),('PC-20','PC Lab 20');

-- ── Tabel pengaturan kontrol lab ────────────────────────────────
CREATE TABLE IF NOT EXISTS control_settings (
  setting_key   VARCHAR(100) PRIMARY KEY,
  setting_value TEXT         NOT NULL,
  updated_at    TIMESTAMP    DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);

-- Nilai default pengaturan
INSERT IGNORE INTO control_settings (setting_key, setting_value) VALUES
('web_filter_enabled', 'true'),
('web_filter_mode',    'blacklist'),
('blacklist',          '["facebook.com","tiktok.com","poki.com"]'),
('whitelist',          '["wikipedia.org","belajar.kemdikbud.go.id","google.com"]'),
('master_volume',      '75'),
('master_muted',       'false'),
('wallpaper_url',      'https://images.unsplash.com/photo-1617042375876-a13e36732a04?q=80&w=2070&auto=format&fit=crop'),
('wallpaper_target',   'both');

-- ── Tabel pengecekan fasilitas (pre/post sesi) ──────────────────
CREATE TABLE IF NOT EXISTS facility_checks (
  id                  INT AUTO_INCREMENT PRIMARY KEY,
  session_id          INT          NULL,
  nis                 VARCHAR(20)  NOT NULL,
  nama_lengkap        VARCHAR(100) NOT NULL,
  pc_name             VARCHAR(50)  NOT NULL,
  check_type          ENUM('pre','post') NOT NULL,
  -- Pre-check items
  cpu_status          ENUM('ok','bad') NULL,
  cpu_note            TEXT NULL,
  monitor_status      ENUM('ok','bad') NULL,
  monitor_note        TEXT NULL,
  desk_status         ENUM('ok','bad') NULL,
  desk_note           TEXT NULL,
  -- Post-check items
  hw_status           ENUM('ok','bad') NULL,
  hw_note             TEXT NULL,
  cleanliness_status  ENUM('ok','bad') NULL,
  cleanliness_note    TEXT NULL,
  account_status      ENUM('ok','bad') NULL,
  account_note        TEXT NULL,
  system_status       ENUM('ok','bad') NULL,
  system_note         TEXT NULL,
  file_status         ENUM('ok','bad') NULL,
  file_note           TEXT NULL,
  created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY (session_id) REFERENCES sessions(id)
    ON DELETE SET NULL ON UPDATE CASCADE
);

-- Tambah kolom kelas ke students jika belum ada
ALTER TABLE students
  MODIFY COLUMN kelas VARCHAR(50) NULL DEFAULT NULL;

-- Tambah status force_ended agar riwayat force logout terbaca akurat
ALTER TABLE sessions
  MODIFY COLUMN status ENUM('active', 'finished', 'force_ended') DEFAULT 'active';

SELECT 'Migration v2 selesai.' AS status;
