-- =====================================================================
-- DAM MASTER AI  ·  Phase 1 database schema  ·  MySQL 8.0+
-- Conventions: CHAR(36) UUID keys for business tables, BIGINT for
-- high-volume time series, UTC DATETIME, utf8mb4. Referential links are
-- enforced in the application layer (no FOREIGN KEY constraints) and
-- every link column is indexed.
-- =====================================================================
SET NAMES utf8mb4;

-- ---------------- Reference catalogues (seeded, code-keyed) ----------------
CREATE TABLE IF NOT EXISTS ref_dam_type (
  code VARCHAR(30) PRIMARY KEY, name VARCHAR(120) NOT NULL, material_group VARCHAR(30) NOT NULL,
  description VARCHAR(500), weaknesses VARCHAR(500), monitoring_priority VARCHAR(500), sort_order INT DEFAULT 0);

CREATE TABLE IF NOT EXISTS ref_dam_function (
  code VARCHAR(30) PRIMARY KEY, name VARCHAR(120) NOT NULL, description VARCHAR(500), monitoring_note VARCHAR(500), sort_order INT DEFAULT 0);

CREATE TABLE IF NOT EXISTS ref_hazard_class (
  code VARCHAR(20) PRIMARY KEY, name VARCHAR(60) NOT NULL, description VARCHAR(500), risk_factor DECIMAL(4,2) NOT NULL);

CREATE TABLE IF NOT EXISTS ref_failure_mode (
  code VARCHAR(30) PRIMARY KEY, name VARCHAR(120) NOT NULL, description VARCHAR(600), applies_to VARCHAR(200),
  early_signs VARCHAR(600), sort_order INT DEFAULT 0);

CREATE TABLE IF NOT EXISTS ref_parameter_group (
  code VARCHAR(10) PRIMARY KEY, name VARCHAR(80) NOT NULL, sort_order INT DEFAULT 0);

CREATE TABLE IF NOT EXISTS ref_parameter (
  code VARCHAR(10) PRIMARY KEY, name VARCHAR(120) NOT NULL, group_code VARCHAR(10) NOT NULL, unit VARCHAR(20) NOT NULL,
  is_load TINYINT(1) NOT NULL DEFAULT 0, is_event TINYINT(1) NOT NULL DEFAULT 0, purpose VARCHAR(400),
  INDEX ix_param_group (group_code));

CREATE TABLE IF NOT EXISTS ref_instrument_type (
  code VARCHAR(30) PRIMARY KEY, name VARCHAR(150) NOT NULL, measures VARCHAR(200), principle VARCHAR(300),
  raw_output VARCHAR(200), eng_unit VARCHAR(40), typical_range VARCHAR(150), used_in VARCHAR(150), sort_order INT DEFAULT 0);

CREATE TABLE IF NOT EXISTS ref_failure_mode_parameter (
  failure_mode_code VARCHAR(30) NOT NULL, parameter_code VARCHAR(10) NOT NULL, role VARCHAR(20) NOT NULL DEFAULT 'Primary',
  PRIMARY KEY (failure_mode_code, parameter_code), INDEX ix_fmp_param (parameter_code));

CREATE TABLE IF NOT EXISTS ref_standard (
  code VARCHAR(30) PRIMARY KEY, title VARCHAR(250) NOT NULL, subject VARCHAR(400), category VARCHAR(60) NOT NULL,
  edition VARCHAR(60) NULL, clauses VARCHAR(400) NULL, verified_by VARCHAR(120) NULL, verified_at DATETIME NULL, sort_order INT DEFAULT 0);

CREATE TABLE IF NOT EXISTS ref_alert_level (
  level TINYINT PRIMARY KEY, name VARCHAR(40) NOT NULL, colour VARCHAR(10) NOT NULL, meaning VARCHAR(300),
  notify_roles VARCHAR(200), channels VARCHAR(120), ack_minutes INT NOT NULL);

CREATE TABLE IF NOT EXISTS ref_interpretation (
  id INT AUTO_INCREMENT PRIMARY KEY, group_code VARCHAR(10) NOT NULL, observation VARCHAR(150) NOT NULL,
  normal_pattern VARCHAR(400), warning_sign VARCHAR(400), critical_sign VARCHAR(400), possible_cause VARCHAR(400), action_text VARCHAR(400),
  INDEX ix_interp_group (group_code));

-- ---------------- Security & audit ----------------
CREATE TABLE IF NOT EXISTS app_user (
  id CHAR(36) PRIMARY KEY, username VARCHAR(60) NOT NULL, full_name VARCHAR(120) NOT NULL,email VARCHAR(255) NULL,
   phone VARCHAR(30) NULL, role VARCHAR(40) NOT NULL,
  password_hash VARCHAR(300) NOT NULL, is_active TINYINT(1) NOT NULL DEFAULT 1, must_change_password TINYINT(1) NOT NULL DEFAULT 0,
  failed_logins INT NOT NULL DEFAULT 0, locked_until DATETIME NULL, last_login_at DATETIME NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, UNIQUE KEY ux_user_name (username));

CREATE TABLE IF NOT EXISTS audit_log (
  id BIGINT AUTO_INCREMENT PRIMARY KEY, at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, user_id CHAR(36) NULL, username VARCHAR(60) NULL,
  module VARCHAR(40) NOT NULL, action VARCHAR(300) NOT NULL, entity VARCHAR(40) NULL, entity_id VARCHAR(64) NULL, detail TEXT NULL,
  INDEX ix_audit_at (at), INDEX ix_audit_entity (entity, entity_id));

-- ---------------- Dams, PFMA, sensors ----------------
CREATE TABLE IF NOT EXISTS dam (
  id CHAR(36) PRIMARY KEY, code VARCHAR(30) NOT NULL, name VARCHAR(150) NOT NULL, state VARCHAR(80), district VARCHAR(80), river VARCHAR(80),
  latitude DECIMAL(9,6) NULL, longitude DECIMAL(9,6) NULL, dam_type_code VARCHAR(30) NOT NULL, function_code VARCHAR(30) NULL, hazard_code VARCHAR(20) NOT NULL,
  height_m DECIMAL(8,2) NULL, crest_length_m DECIMAL(10,2) NULL, frl_m DECIMAL(9,3) NULL, mwl_m DECIMAL(9,3) NULL, mddl_m DECIMAL(9,3) NULL,
  crest_level_m DECIMAL(9,3) NULL, gross_storage_mcm DECIMAL(12,3) NULL, seismic_zone VARCHAR(5) NULL, is_specified TINYINT(1) NOT NULL DEFAULT 1,
  life_phase VARCHAR(30) NOT NULL DEFAULT 'Long-term operation', eap_status VARCHAR(40) NULL, owner_name VARCHAR(150) NULL,
  is_active TINYINT(1) NOT NULL DEFAULT 1, created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at DATETIME NULL,
  UNIQUE KEY ux_dam_code (code), INDEX ix_dam_type (dam_type_code));

CREATE TABLE IF NOT EXISTS pfma (
  id CHAR(36) PRIMARY KEY, dam_id CHAR(36) NOT NULL, failure_mode_code VARCHAR(30) NOT NULL, location VARCHAR(200), description VARCHAR(1000),
  likelihood VARCHAR(20) NULL, consequence VARCHAR(20) NULL, status VARCHAR(20) NOT NULL DEFAULT 'Active',
  created_by VARCHAR(120), created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at DATETIME NULL,
  INDEX ix_pfma_dam (dam_id), INDEX ix_pfma_fm (failure_mode_code));

CREATE TABLE IF NOT EXISTS sensor (
  id CHAR(36) PRIMARY KEY, dam_id CHAR(36) NOT NULL, code VARCHAR(40) NOT NULL, parameter_code VARCHAR(10) NOT NULL, instrument_type_code VARCHAR(30) NULL,
  location VARCHAR(200), elevation_m DECIMAL(9,3) NULL, eng_unit VARCHAR(20) NOT NULL, expected_min DOUBLE NULL, expected_max DOUBLE NULL,
  direction VARCHAR(10) NOT NULL DEFAULT 'Rising', reading_interval_min INT NOT NULL DEFAULT 360, calibration_due DATE NULL,
  status VARCHAR(20) NOT NULL DEFAULT 'Active', created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at DATETIME NULL,
  UNIQUE KEY ux_sensor_code (code), INDEX ix_sensor_dam (dam_id), INDEX ix_sensor_param (parameter_code));

CREATE TABLE IF NOT EXISTS pfma_sensor (
  pfma_id CHAR(36) NOT NULL, sensor_id CHAR(36) NOT NULL, PRIMARY KEY (pfma_id, sensor_id), INDEX ix_ps_sensor (sensor_id));

CREATE TABLE IF NOT EXISTS sensor_threshold (
  id CHAR(36) PRIMARY KEY, sensor_id CHAR(36) NOT NULL, alert_value DOUBLE NULL, alarm_value DOUBLE NULL, emergency_value DOUBLE NULL,
  rate_limit_per_day DOUBLE NULL, z_limit DECIMAL(4,2) NOT NULL DEFAULT 2.50, persistence INT NOT NULL DEFAULT 2,
  method VARCHAR(30) NOT NULL DEFAULT 'Design', basis VARCHAR(600) NULL, created_by VARCHAR(120), created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  approved_by VARCHAR(120) NULL, approved_at DATETIME NULL, is_current TINYINT(1) NOT NULL DEFAULT 0,
  INDEX ix_thr_sensor (sensor_id, is_current));

CREATE TABLE IF NOT EXISTS gateway (
  id CHAR(36) PRIMARY KEY, code VARCHAR(40) NOT NULL, dam_id CHAR(36) NOT NULL, key_hash VARCHAR(200) NOT NULL, is_active TINYINT(1) NOT NULL DEFAULT 1,
  last_seen_at DATETIME NULL, created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, UNIQUE KEY ux_gw_code (code), INDEX ix_gw_dam (dam_id));

-- ---------------- Readings & imports ----------------
CREATE TABLE IF NOT EXISTS reading (
  id BIGINT AUTO_INCREMENT PRIMARY KEY, sensor_id CHAR(36) NOT NULL, observed_at DATETIME NOT NULL, value DOUBLE NOT NULL, raw_value DOUBLE NULL,
  quality CHAR(1) NOT NULL DEFAULT 'G', qc_rule VARCHAR(40) NULL, import_id CHAR(36) NULL, created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY ux_reading (sensor_id, observed_at), INDEX ix_reading_import (import_id));

CREATE TABLE IF NOT EXISTS import_batch (
  id CHAR(36) PRIMARY KEY, source VARCHAR(20) NOT NULL, file_name VARCHAR(255) NULL, created_by VARCHAR(120), created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  rows_read INT NOT NULL DEFAULT 0, good INT NOT NULL DEFAULT 0, suspect INT NOT NULL DEFAULT 0, bad INT NOT NULL DEFAULT 0,
  rejected INT NOT NULL DEFAULT 0, quarantined INT NOT NULL DEFAULT 0, duplicates INT NOT NULL DEFAULT 0, dams_upserted INT NOT NULL DEFAULT 0, sensors_upserted INT NOT NULL DEFAULT 0,
  INDEX ix_import_at (created_at));

CREATE TABLE IF NOT EXISTS import_issue (
  id BIGINT AUTO_INCREMENT PRIMARY KEY, import_id CHAR(36) NOT NULL, sheet VARCHAR(30), row_no INT, item_code VARCHAR(60), outcome VARCHAR(20), rule_code VARCHAR(40), message VARCHAR(400),
  INDEX ix_issue_import (import_id));

CREATE TABLE IF NOT EXISTS quarantine (
  id CHAR(36) PRIMARY KEY, import_id CHAR(36) NULL, dam_code VARCHAR(30), sensor_code VARCHAR(40), parameter_code VARCHAR(10), observed_at DATETIME NULL,
  value DOUBLE NULL, unit VARCHAR(20), status VARCHAR(20) NOT NULL DEFAULT 'Held', created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, INDEX ix_q_sensor (sensor_code, status));

-- ---------------- Alerts ----------------
CREATE TABLE IF NOT EXISTS alert (
  id CHAR(36) PRIMARY KEY, dam_id CHAR(36) NOT NULL, sensor_id CHAR(36) NULL, pfma_id CHAR(36) NULL, level TINYINT NOT NULL,
  rule_type VARCHAR(20) NOT NULL, title VARCHAR(250) NOT NULL, detail VARCHAR(1500) NULL, value DOUBLE NULL, limit_value DOUBLE NULL,
  first_at DATETIME NOT NULL, last_at DATETIME NOT NULL, occurrences INT NOT NULL DEFAULT 1, status VARCHAR(20) NOT NULL DEFAULT 'Open',
  ack_by VARCHAR(120) NULL, ack_at DATETIME NULL, ack_note VARCHAR(600) NULL, closed_by VARCHAR(120) NULL, closed_at DATETIME NULL,
  disposition VARCHAR(30) NULL, close_note VARCHAR(600) NULL, created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX ix_alert_dam (dam_id, status), INDEX ix_alert_sensor (sensor_id, rule_type, status), INDEX ix_alert_pfma (pfma_id, status));
