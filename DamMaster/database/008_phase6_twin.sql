-- =====================================================================
-- DAM MASTER AI · Phase 6 — GIS map, live section (digital twin), InSAR and
-- survey datasets, document files with full-text search, design-to-threshold link.
-- MySQL 8. Re-runnable.
-- =====================================================================
SET NAMES utf8mb4;
DROP PROCEDURE IF EXISTS dm_add_column6;
DELIMITER $$
CREATE PROCEDURE dm_add_column6(IN tbl VARCHAR(64), IN col VARCHAR(64), IN ddl VARCHAR(500))
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = col) THEN
    SET @s = CONCAT('ALTER TABLE ', tbl, ' ADD COLUMN ', ddl); PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;
  END IF;
END$$
DELIMITER ;
CALL dm_add_column6('sensor', 'latitude', 'latitude DECIMAL(9,6) NULL');
CALL dm_add_column6('sensor', 'longitude', 'longitude DECIMAL(9,6) NULL');
CALL dm_add_column6('sensor', 'offset_m', 'offset_m DOUBLE NULL');            -- distance downstream from the upstream face / heel (m)
CALL dm_add_column6('sensor', 'chainage_m', 'chainage_m DOUBLE NULL');        -- position along the dam axis (m)
CALL dm_add_column6('alert', 'survey_id', 'survey_id CHAR(36) NULL');
DROP PROCEDURE IF EXISTS dm_add_column6;

-- InSAR ground-movement products and survey campaigns (drone / total station / GNSS)
CREATE TABLE IF NOT EXISTS survey_dataset (
  id CHAR(36) PRIMARY KEY, dam_id CHAR(36) NOT NULL, kind VARCHAR(10) NOT NULL,               -- 'InSAR' or 'Survey'
  name VARCHAR(200) NOT NULL, provider VARCHAR(150) NULL, epoch_date DATE NULL, period_from DATE NULL, period_to DATE NULL,
  unit VARCHAR(20) NOT NULL, threshold DOUBLE NULL, point_count INT NOT NULL DEFAULT 0, notes VARCHAR(600) NULL,
  created_by VARCHAR(120), created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, INDEX ix_sd_dam (dam_id, kind));

CREATE TABLE IF NOT EXISTS survey_point (
  id BIGINT AUTO_INCREMENT PRIMARY KEY, dataset_id CHAR(36) NOT NULL, point_id VARCHAR(60) NOT NULL,
  latitude DECIMAL(9,6) NOT NULL, longitude DECIMAL(9,6) NOT NULL, value DOUBLE NOT NULL, coherence DOUBLE NULL, label VARCHAR(100) NULL,
  INDEX ix_sp_ds (dataset_id), UNIQUE KEY ux_sp (dataset_id, point_id));

-- uploaded files of registered documents, and their text split into passages for search
CREATE TABLE IF NOT EXISTS doc_file (
  id CHAR(36) PRIMARY KEY, document_id CHAR(36) NOT NULL, dam_id CHAR(36) NOT NULL, file_name VARCHAR(255) NOT NULL, content_type VARCHAR(100) NULL,
  size_bytes BIGINT NOT NULL, sha256 CHAR(64) NOT NULL, storage_path VARCHAR(400) NOT NULL, pages INT NULL, text_chars INT NOT NULL DEFAULT 0,
  extracted_ok TINYINT(1) NOT NULL DEFAULT 0, extract_note VARCHAR(300) NULL, created_by VARCHAR(120), created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX ix_df_doc (document_id), INDEX ix_df_dam (dam_id));

CREATE TABLE IF NOT EXISTS doc_chunk (
  id BIGINT AUTO_INCREMENT PRIMARY KEY, file_id CHAR(36) NOT NULL, document_id CHAR(36) NOT NULL, dam_id CHAR(36) NOT NULL,
  seq INT NOT NULL, page INT NULL, text TEXT NOT NULL, INDEX ix_dc_file (file_id), FULLTEXT KEY ft_dc_text (text)) ENGINE=InnoDB;
