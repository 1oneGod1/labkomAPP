-- ============================================================================
-- Activity Monitoring System - Database Schema
-- ============================================================================
-- Tabel untuk menyimpan log aktivitas siswa secara real-time
-- Termasuk: active window, browser URLs, dan running applications
-- ============================================================================

CREATE TABLE IF NOT EXISTS activity_logs (
  id INT PRIMARY KEY AUTO_INCREMENT,
  
  -- Identifikasi PC dan Siswa
  pc_name VARCHAR(100) NOT NULL,
  student_id INT DEFAULT NULL,
  student_name VARCHAR(100) DEFAULT NULL,
  session_id INT DEFAULT NULL,
  
  -- Tipe Aktivitas
  activity_type ENUM('window_change', 'browser_url', 'app_list', 'idle') NOT NULL,
  
  -- Data Window Activity
  window_title VARCHAR(500) DEFAULT NULL,
  process_name VARCHAR(200) DEFAULT NULL,
  process_path VARCHAR(500) DEFAULT NULL,
  
  -- Data Browser Activity  
  browser_name VARCHAR(50) DEFAULT NULL,
  url VARCHAR(2000) DEFAULT NULL,
  url_domain VARCHAR(200) DEFAULT NULL,
  page_title VARCHAR(500) DEFAULT NULL,
  
  -- Running Applications (JSON array)
  running_apps JSON DEFAULT NULL,
  
  -- Metadata & Categorization
  is_productive BOOLEAN DEFAULT NULL,
  category VARCHAR(50) DEFAULT NULL COMMENT 'e.g., coding, browsing, gaming, office',
  tags JSON DEFAULT NULL COMMENT 'Array of tags for filtering',
  
  -- Durasi (opsional, untuk agregasi)
  duration_seconds INT DEFAULT NULL COMMENT 'Berapa lama window/app aktif',
  
  -- Timestamps
  activity_at DATETIME NOT NULL COMMENT 'Waktu aktivitas terjadi',
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  
  -- Indexes untuk performa query
  INDEX idx_pc_activity (pc_name, activity_at),
  INDEX idx_student_activity (student_id, activity_at),
  INDEX idx_session (session_id),
  INDEX idx_activity_type (activity_type),
  INDEX idx_url_domain (url_domain),
  INDEX idx_category (category),
  INDEX idx_created_at (created_at),
  
  -- Foreign Keys
  FOREIGN KEY (student_id) 
    REFERENCES students(id) 
    ON DELETE SET NULL 
    ON UPDATE CASCADE,
    
  FOREIGN KEY (session_id) 
    REFERENCES student_sessions(id) 
    ON DELETE SET NULL 
    ON UPDATE CASCADE
    
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='Log aktivitas siswa untuk monitoring real-time';

-- ============================================================================
-- Tabel untuk kategori aplikasi dan website (whitelist/blacklist)
-- ============================================================================

CREATE TABLE IF NOT EXISTS activity_categories (
  id INT PRIMARY KEY AUTO_INCREMENT,
  
  -- Pattern matching
  pattern_type ENUM('process_name', 'url_domain', 'window_title') NOT NULL,
  pattern VARCHAR(200) NOT NULL COMMENT 'Regex atau exact match pattern',
  match_method ENUM('exact', 'contains', 'regex', 'starts_with') DEFAULT 'contains',
  
  -- Kategori
  category VARCHAR(50) NOT NULL COMMENT 'coding, browsing, gaming, office, etc',
  is_productive BOOLEAN DEFAULT NULL,
  is_allowed BOOLEAN DEFAULT TRUE COMMENT 'FALSE = blacklisted',
  
  -- Alert settings
  should_alert BOOLEAN DEFAULT FALSE,
  alert_message VARCHAR(200) DEFAULT NULL,
  
  -- Metadata
  description VARCHAR(200) DEFAULT NULL,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  
  -- Indexes
  INDEX idx_pattern_type (pattern_type),
  INDEX idx_category (category),
  INDEX idx_is_allowed (is_allowed),
  UNIQUE KEY unique_pattern (pattern_type, pattern)
  
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='Aturan kategorisasi dan filtering aktivitas';

-- ============================================================================
-- Insert default categories (contoh)
-- ============================================================================

INSERT INTO activity_categories (pattern_type, pattern, match_method, category, is_productive, is_allowed, description) VALUES
-- Productive Apps
('process_name', 'Code.exe', 'exact', 'coding', TRUE, TRUE, 'Visual Studio Code'),
('process_name', 'devenv.exe', 'exact', 'coding', TRUE, TRUE, 'Visual Studio'),
('process_name', 'idea64.exe', 'contains', 'coding', TRUE, TRUE, 'IntelliJ IDEA'),
('process_name', 'eclipse.exe', 'exact', 'coding', TRUE, TRUE, 'Eclipse IDE'),
('process_name', 'WINWORD.EXE', 'exact', 'office', TRUE, TRUE, 'Microsoft Word'),
('process_name', 'EXCEL.EXE', 'exact', 'office', TRUE, TRUE, 'Microsoft Excel'),
('process_name', 'POWERPNT.EXE', 'exact', 'office', TRUE, TRUE, 'Microsoft PowerPoint'),

-- Browsers (neutral - depends on content)
('process_name', 'chrome.exe', 'exact', 'browsing', NULL, TRUE, 'Google Chrome'),
('process_name', 'msedge.exe', 'exact', 'browsing', NULL, TRUE, 'Microsoft Edge'),
('process_name', 'firefox.exe', 'exact', 'browsing', NULL, TRUE, 'Mozilla Firefox'),

-- Productive Websites
('url_domain', 'github.com', 'exact', 'coding', TRUE, TRUE, 'GitHub'),
('url_domain', 'stackoverflow.com', 'exact', 'coding', TRUE, TRUE, 'Stack Overflow'),
('url_domain', 'docs.microsoft.com', 'contains', 'learning', TRUE, TRUE, 'Microsoft Docs'),
('url_domain', 'developer.mozilla.org', 'contains', 'learning', TRUE, TRUE, 'MDN Web Docs'),
('url_domain', 'w3schools.com', 'exact', 'learning', TRUE, TRUE, 'W3Schools'),

-- Unproductive/Gaming (with alerts)
('process_name', 'Steam.exe', 'exact', 'gaming', FALSE, FALSE, 'Steam Gaming Platform'),
('process_name', 'League of Legends.exe', 'contains', 'gaming', FALSE, FALSE, 'League of Legends'),
('process_name', 'Valorant.exe', 'contains', 'gaming', FALSE, FALSE, 'Valorant'),
('process_name', 'discord.exe', 'exact', 'communication', FALSE, TRUE, 'Discord - allowed but monitored'),
('url_domain', 'youtube.com', 'exact', 'entertainment', FALSE, TRUE, 'YouTube - context dependent'),
('url_domain', 'facebook.com', 'exact', 'social_media', FALSE, TRUE, 'Facebook'),
('url_domain', 'instagram.com', 'exact', 'social_media', FALSE, TRUE, 'Instagram'),
('url_domain', 'tiktok.com', 'exact', 'social_media', FALSE, FALSE, 'TikTok - blocked'),
('url_domain', 'twitter.com', 'exact', 'social_media', FALSE, TRUE, 'Twitter/X');

-- Update alert settings for blacklisted items
UPDATE activity_categories 
SET should_alert = TRUE, 
    alert_message = CONCAT('⚠️ Aplikasi/website tidak diperbolehkan: ', description)
WHERE is_allowed = FALSE;

-- ============================================================================
-- View untuk activity summary per siswa
-- ============================================================================

CREATE OR REPLACE VIEW activity_summary AS
SELECT 
  al.student_id,
  al.student_name,
  al.pc_name,
  al.session_id,
  al.category,
  COUNT(*) as activity_count,
  MIN(al.activity_at) as first_activity,
  MAX(al.activity_at) as last_activity,
  SUM(CASE WHEN al.is_productive = TRUE THEN 1 ELSE 0 END) as productive_count,
  SUM(CASE WHEN al.is_productive = FALSE THEN 1 ELSE 0 END) as unproductive_count,
  GROUP_CONCAT(DISTINCT al.url_domain ORDER BY al.activity_at DESC SEPARATOR ', ') as visited_domains,
  GROUP_CONCAT(DISTINCT al.process_name ORDER BY al.activity_at DESC SEPARATOR ', ') as used_apps
FROM activity_logs al
WHERE al.activity_at >= DATE_SUB(NOW(), INTERVAL 24 HOUR)
GROUP BY al.student_id, al.student_name, al.pc_name, al.session_id, al.category
ORDER BY last_activity DESC;

-- ============================================================================
-- Stored Procedure untuk cleanup old activity logs
-- ============================================================================

DELIMITER $$

CREATE PROCEDURE cleanup_old_activity_logs(IN days_to_keep INT)
BEGIN
  DELETE FROM activity_logs 
  WHERE created_at < DATE_SUB(NOW(), INTERVAL days_to_keep DAY);
  
  SELECT ROW_COUNT() as deleted_rows;
END$$

DELIMITER ;

-- ============================================================================
-- Event untuk auto-cleanup setiap hari (opsional)
-- ============================================================================

-- Uncomment untuk enable auto-cleanup
-- SET GLOBAL event_scheduler = ON;
-- 
-- CREATE EVENT IF NOT EXISTS daily_activity_cleanup
-- ON SCHEDULE EVERY 1 DAY
-- STARTS (TIMESTAMP(CURRENT_DATE) + INTERVAL 1 DAY + INTERVAL 2 HOUR)
-- DO
--   CALL cleanup_old_activity_logs(30);

-- ============================================================================
-- Indexes tambahan untuk performa (jika diperlukan)
-- ============================================================================

-- Composite index untuk query yang sering digunakan
-- ALTER TABLE activity_logs ADD INDEX idx_student_session_time (student_id, session_id, activity_at);
-- ALTER TABLE activity_logs ADD INDEX idx_pc_type_time (pc_name, activity_type, activity_at);

-- ============================================================================
-- Grants (adjust sesuai user database Anda)
-- ============================================================================

-- GRANT SELECT, INSERT, UPDATE, DELETE ON labkom.activity_logs TO 'labkom_user'@'localhost';
-- GRANT SELECT, INSERT, UPDATE ON labkom.activity_categories TO 'labkom_user'@'localhost';
-- GRANT SELECT ON labkom.activity_summary TO 'labkom_user'@'localhost';
-- GRANT EXECUTE ON PROCEDURE labkom.cleanup_old_activity_logs TO 'labkom_user'@'localhost';

-- ============================================================================
-- Testing queries (untuk verifikasi)
-- ============================================================================

-- Check if tables created successfully
-- SELECT TABLE_NAME, TABLE_ROWS, AUTO_INCREMENT 
-- FROM information_schema.TABLES 
-- WHERE TABLE_SCHEMA = 'labkom' 
--   AND TABLE_NAME IN ('activity_logs', 'activity_categories');

-- View default categories
-- SELECT * FROM activity_categories ORDER BY category, is_productive DESC;

-- ============================================================================
-- SELESAI
-- ============================================================================
-- Run this script dengan:
-- mysql -u root -p labkom < activity-logs-schema.sql
-- ============================================================================
