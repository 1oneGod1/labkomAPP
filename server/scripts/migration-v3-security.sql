-- ============================================================
-- Migration v3 - Security hardening (admin audit logs)
-- ============================================================

USE labkom_db;

CREATE TABLE IF NOT EXISTS admin_audit_logs (
  id            BIGINT AUTO_INCREMENT PRIMARY KEY,
  action        VARCHAR(120)  NOT NULL,
  method        VARCHAR(10)   NOT NULL,
  path          VARCHAR(255)  NOT NULL,
  status_code   INT           NULL,
  success       TINYINT(1)    NULL,
  ip_address    VARCHAR(64)   NULL,
  user_agent    VARCHAR(255)  NULL,
  metadata_json JSON          NULL,
  created_at    TIMESTAMP     DEFAULT CURRENT_TIMESTAMP,
  INDEX idx_created_at (created_at),
  INDEX idx_action (action),
  INDEX idx_path (path)
);

SELECT 'Migration v3 security selesai.' AS status;
