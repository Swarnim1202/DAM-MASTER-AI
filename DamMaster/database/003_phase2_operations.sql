-- =====================================================================
-- DAM MASTER AI · Phase 2 — inspections, reading schedules, EAP contacts,
-- notifications and alert escalation. MySQL 8. Re-runnable.
-- =====================================================================
SET NAMES utf8mb4;

-- helper: add a column only if it does not exist (MySQL has no ADD COLUMN IF NOT EXISTS)
DROP PROCEDURE IF EXISTS dm_add_column;
DELIMITER $$
CREATE PROCEDURE dm_add_column(IN tbl VARCHAR(64), IN col VARCHAR(64), IN ddl VARCHAR(500))
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = col) THEN
    SET @s = CONCAT('ALTER TABLE ', tbl, ' ADD COLUMN ', ddl);
    PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;
  END IF;
END$$
DELIMITER ;

CALL dm_add_column('alert', 'ack_due_at', 'ack_due_at DATETIME NULL');
CALL dm_add_column('alert', 'escalation_step', 'escalation_step INT NOT NULL DEFAULT 0');
CALL dm_add_column('alert', 'inspection_id', 'inspection_id CHAR(36) NULL');
DROP PROCEDURE IF EXISTS dm_add_column;

-- ---------------- Reading schedule by life phase ----------------
CREATE TABLE IF NOT EXISTS ref_reading_frequency (
  group_code VARCHAR(10) NOT NULL, life_phase VARCHAR(30) NOT NULL, interval_hours DECIMAL(8,2) NULL, note VARCHAR(200) NULL,
  PRIMARY KEY (group_code, life_phase));

-- ---------------- Inspections ----------------
CREATE TABLE IF NOT EXISTS ref_inspection_item (
  code VARCHAR(20) PRIMARY KEY, material_group VARCHAR(30) NOT NULL, component VARCHAR(60) NOT NULL, item VARCHAR(300) NOT NULL, sort_order INT DEFAULT 0,
  INDEX ix_item_group (material_group));

CREATE TABLE IF NOT EXISTS inspection (
  id CHAR(36) PRIMARY KEY, dam_id CHAR(36) NOT NULL, inspection_type VARCHAR(30) NOT NULL, scheduled_date DATE NOT NULL,
  status VARCHAR(20) NOT NULL DEFAULT 'Planned', inspector VARCHAR(120) NULL, started_at DATETIME NULL, completed_at DATETIME NULL,
  overall_condition VARCHAR(20) NULL, summary VARCHAR(2000) NULL, trigger_alert_id CHAR(36) NULL,
  created_by VARCHAR(120), created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX ix_insp_dam (dam_id, status), INDEX ix_insp_date (scheduled_date));

CREATE TABLE IF NOT EXISTS inspection_check (
  inspection_id CHAR(36) NOT NULL, item_code VARCHAR(20) NOT NULL, result VARCHAR(20) NOT NULL, remark VARCHAR(500) NULL,
  checked_by VARCHAR(120), checked_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, PRIMARY KEY (inspection_id, item_code));

CREATE TABLE IF NOT EXISTS inspection_finding (
  id CHAR(36) PRIMARY KEY, inspection_id CHAR(36) NOT NULL, dam_id CHAR(36) NOT NULL, component VARCHAR(60) NOT NULL, location VARCHAR(200) NULL,
  description VARCHAR(1000) NOT NULL, severity VARCHAR(20) NOT NULL, sensor_id CHAR(36) NULL, recommendation VARCHAR(1000) NULL,
  status VARCHAR(20) NOT NULL DEFAULT 'Open', due_date DATE NULL, alert_id CHAR(36) NULL,
  created_by VARCHAR(120), created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  closed_by VARCHAR(120) NULL, closed_at DATETIME NULL, closure_note VARCHAR(1000) NULL,
  INDEX ix_find_insp (inspection_id), INDEX ix_find_dam (dam_id, status), INDEX ix_find_sensor (sensor_id));

-- ---------------- EAP contacts & notifications ----------------
CREATE TABLE IF NOT EXISTS eap_contact (
  id CHAR(36) PRIMARY KEY, dam_id CHAR(36) NOT NULL, name VARCHAR(120) NOT NULL, organisation VARCHAR(150) NULL, eap_role VARCHAR(120) NULL,
  phone VARCHAR(30) NULL, email VARCHAR(150) NULL, min_level TINYINT NOT NULL DEFAULT 2, escalation_order INT NOT NULL DEFAULT 1,
  is_active TINYINT(1) NOT NULL DEFAULT 1, created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, INDEX ix_eap_dam (dam_id, is_active));

CREATE TABLE IF NOT EXISTS notification (
  id BIGINT AUTO_INCREMENT PRIMARY KEY, alert_id CHAR(36) NULL, dam_id CHAR(36) NULL, level TINYINT NOT NULL DEFAULT 0,
  channel VARCHAR(10) NOT NULL, recipient VARCHAR(150) NOT NULL, recipient_name VARCHAR(150) NULL, subject VARCHAR(250) NOT NULL, body VARCHAR(2000) NOT NULL,
  escalation_step INT NOT NULL DEFAULT 0, status VARCHAR(20) NOT NULL DEFAULT 'Queued', attempts INT NOT NULL DEFAULT 0, error VARCHAR(500) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, sent_at DATETIME NULL, read_at DATETIME NULL,
  INDEX ix_notif_status (status, channel), INDEX ix_notif_recipient (recipient, read_at), INDEX ix_notif_alert (alert_id));

-- ---------------- Seeds ----------------
DELETE FROM ref_reading_frequency;
INSERT INTO ref_reading_frequency (group_code, life_phase, interval_hours, note) VALUES
('A','Construction',24,'Daily'),('B','Construction',NULL,'Not applicable before impounding'),('C','Construction',24,'Daily (weekly if manual)'),('D','Construction',24,'Daily'),('E','Construction',24,'Daily (weekly if manual)'),('F','Construction',24,'Daily'),
('A','First filling',1,'Hourly — most critical period'),('B','First filling',1,'Hourly (daily if manual)'),('C','First filling',1,'Hourly (daily if manual)'),('D','First filling',6,'Every 6 h (daily if manual)'),('E','First filling',6,'Every 6 h (weekly if manual)'),('F','First filling',6,'Every 6 h'),
('A','Initial operation',1,'Hourly'),('B','Initial operation',1,'Hourly (weekly if manual)'),('C','Initial operation',6,'Every 6 h (weekly if manual)'),('D','Initial operation',6,'Every 6 h (weekly if manual)'),('E','Initial operation',24,'Daily (monthly if manual)'),('F','Initial operation',6,'Every 6 h'),
('A','Long-term operation',1,'Hourly'),('B','Long-term operation',1,'Hourly (weekly if manual)'),('C','Long-term operation',6,'Every 6 h (weekly–monthly if manual)'),('D','Long-term operation',6,'Every 6 h (monthly if manual)'),('E','Long-term operation',24,'Daily (quarterly if manual)'),('F','Long-term operation',6,'Every 6 h'),
('G','Construction',NULL,'Event-triggered'),('G','First filling',NULL,'Event-triggered'),('G','Initial operation',NULL,'Event-triggered'),('G','Long-term operation',NULL,'Event-triggered'),
('H','Construction',NULL,'On operation'),('H','First filling',NULL,'On operation'),('H','Initial operation',NULL,'On operation'),('H','Long-term operation',NULL,'On operation');

DELETE FROM ref_inspection_item;
INSERT INTO ref_inspection_item (code, material_group, component, item, sort_order) VALUES
('ALL-01','All','Reservoir & rim','Reservoir level, debris, slides or cracks on the rim',1),
('ALL-02','All','Spillway & gates','Spillway clear of debris; gates operated through full travel; hoists, ropes and seals in order',2),
('ALL-03','All','Spillway & gates','Standby power (DG) started and on load; fuel stock adequate',3),
('ALL-04','All','Spillway & gates','Chute / stilling basin: cracks, cavitation damage, joint displacement, scour downstream',4),
('ALL-05','All','Instrumentation','All instruments readable; data logger and communication working; calibration dates',5),
('ALL-06','All','Downstream area','New wet areas, springs, boils, sinkholes, changes in seepage colour',6),
('ALL-07','All','Access & communication','Access roads, lighting, communication to control room and downstream authorities',7),
('ALL-08','All','EAP','EAP contact list current; warning systems tested',8),
('CON-01','Concrete','Crest & faces','Cracks, spalling, efflorescence / leaching deposits on upstream and downstream faces',20),
('CON-02','Concrete','Galleries','Gallery seepage, drain flows, calcite deposits, wet joints, crack growth',21),
('CON-03','Concrete','Foundation & abutments','Seepage or movement at abutments and toe; condition of foundation drains',22),
('CON-04','Concrete','Joints','Contraction joint opening, offsets, water-stop leakage',23),
('EMB-01','Embankment','Crest','Crest settlement, longitudinal or transverse cracks, depressions',40),
('EMB-02','Embankment','Upstream slope','Riprap displacement, slides, erosion, animal burrows, vegetation',41),
('EMB-03','Embankment','Downstream slope & toe','Wet patches, bulging, slides, toe drain flow and turbidity',42),
('EMB-04','Embankment','Seepage measurement','Weir flows and clarity; relief wells functioning',43),
('MAS-01','Masonry','Faces','Mortar loss, open joints, leaching, vegetation in joints',60);

-- user contact details for notifications
DROP PROCEDURE IF EXISTS dm_add_column2;
DELIMITER $$
CREATE PROCEDURE dm_add_column2(IN tbl VARCHAR(64), IN col VARCHAR(64), IN ddl VARCHAR(500))
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = col) THEN
    SET @s = CONCAT('ALTER TABLE ', tbl, ' ADD COLUMN ', ddl);
    PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;
  END IF;
END$$
DELIMITER ;
CALL dm_add_column2('app_user', 'email', 'email VARCHAR(150) NULL');
CALL dm_add_column2('app_user', 'phone', 'phone VARCHAR(30) NULL');
DROP PROCEDURE IF EXISTS dm_add_column2;
