-- =====================================================================
-- DAM MASTER AI · Phase 7 — field (offline) inspections with photos, OCR,
-- single sign-on, federation of state instances. MySQL 8. Re-runnable.
-- =====================================================================
SET NAMES utf8mb4;
DROP PROCEDURE IF EXISTS dm_add_column7;
DELIMITER $$
CREATE PROCEDURE dm_add_column7(IN tbl VARCHAR(64), IN col VARCHAR(64), IN ddl VARCHAR(500))
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = col) THEN
    SET @s = CONCAT('ALTER TABLE ', tbl, ' ADD COLUMN ', ddl); PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;
  END IF;
END$$
DELIMITER ;
CALL dm_add_column7('doc_file', 'ocr_used', 'ocr_used TINYINT(1) NOT NULL DEFAULT 0');
CALL dm_add_column7('app_user', 'sso_subject', 'sso_subject VARCHAR(200) NULL');
DROP PROCEDURE IF EXISTS dm_add_column7;

-- photos attached to inspection findings (taken in the field, possibly offline)
CREATE TABLE IF NOT EXISTS finding_photo (
  id CHAR(36) PRIMARY KEY, finding_id CHAR(36) NOT NULL, inspection_id CHAR(36) NOT NULL, client_ref VARCHAR(64) NULL,
  file_name VARCHAR(255) NOT NULL, content_type VARCHAR(50) NOT NULL, size_bytes BIGINT NOT NULL, storage_path VARCHAR(400) NOT NULL,
  width INT NULL, height INT NULL, latitude DECIMAL(9,6) NULL, longitude DECIMAL(9,6) NULL, taken_at DATETIME NULL, caption VARCHAR(300) NULL,
  created_by VARCHAR(120), created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX ix_fp_finding (finding_id), UNIQUE KEY ux_fp_client (client_ref));

-- operations replayed from offline devices; client_op_id makes every replay idempotent
CREATE TABLE IF NOT EXISTS field_op (
  client_op_id VARCHAR(64) PRIMARY KEY, user_name VARCHAR(120) NOT NULL, kind VARCHAR(20) NOT NULL, inspection_id CHAR(36) NULL,
  entity_id CHAR(36) NULL, ok TINYINT(1) NOT NULL, error VARCHAR(400) NULL, received_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP);
