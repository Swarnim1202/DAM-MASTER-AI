-- =====================================================================
-- DAM MASTER AI · Phase 4 — live IoT (MQTT), gateway health, message log,
-- daily aggregates, job tracking. MySQL 8. Re-runnable.
-- =====================================================================
SET NAMES utf8mb4;
DROP PROCEDURE IF EXISTS dm_add_column4;
DELIMITER $$
CREATE PROCEDURE dm_add_column4(IN tbl VARCHAR(64), IN col VARCHAR(64), IN ddl VARCHAR(500))
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = col) THEN
    SET @s = CONCAT('ALTER TABLE ', tbl, ' ADD COLUMN ', ddl); PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;
  END IF;
END$$
DELIMITER ;
CALL dm_add_column4('gateway', 'expected_interval_min', 'expected_interval_min INT NOT NULL DEFAULT 60');
CALL dm_add_column4('gateway', 'transport', 'transport VARCHAR(10) NULL');
CALL dm_add_column4('gateway', 'last_message_at', 'last_message_at DATETIME NULL');
CALL dm_add_column4('gateway', 'last_status_at', 'last_status_at DATETIME NULL');
CALL dm_add_column4('gateway', 'battery_v', 'battery_v DOUBLE NULL');
CALL dm_add_column4('gateway', 'signal_dbm', 'signal_dbm INT NULL');
CALL dm_add_column4('gateway', 'buffered_count', 'buffered_count INT NULL');
CALL dm_add_column4('gateway', 'firmware', 'firmware VARCHAR(40) NULL');
CALL dm_add_column4('alert', 'gateway_id', 'gateway_id CHAR(36) NULL');
DROP PROCEDURE IF EXISTS dm_add_column4;

-- index on alert.gateway_id (only if missing)
SET @x = (SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'alert' AND INDEX_NAME = 'ix_alert_gateway');
SET @s = IF(@x = 0, 'CREATE INDEX ix_alert_gateway ON alert (gateway_id, status)', 'SELECT 1');
PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;

-- one row per received telemetry message — makes QoS-1 redelivery and HTTP retries idempotent
CREATE TABLE IF NOT EXISTS gateway_message (
  id BIGINT AUTO_INCREMENT PRIMARY KEY, gateway_id CHAR(36) NOT NULL, message_id VARCHAR(100) NOT NULL, transport VARCHAR(10) NOT NULL,
  received_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, readings INT NOT NULL DEFAULT 0, accepted INT NOT NULL DEFAULT 0, rejected INT NOT NULL DEFAULT 0,
  quarantined INT NOT NULL DEFAULT 0, duplicates INT NOT NULL DEFAULT 0, late INT NOT NULL DEFAULT 0, max_latency_s INT NULL, error VARCHAR(400) NULL,
  UNIQUE KEY ux_gw_msg (gateway_id, message_id), INDEX ix_gwm_time (gateway_id, received_at));

CREATE TABLE IF NOT EXISTS gateway_status_log (
  id BIGINT AUTO_INCREMENT PRIMARY KEY, gateway_id CHAR(36) NOT NULL, at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  battery_v DOUBLE NULL, signal_dbm INT NULL, buffered_count INT NULL, uptime_s BIGINT NULL, firmware VARCHAR(40) NULL,
  INDEX ix_gws (gateway_id, at));

-- daily aggregates per sensor (charts over long periods, availability, fast summaries)
CREATE TABLE IF NOT EXISTS reading_daily (
  sensor_id CHAR(36) NOT NULL, day DATE NOT NULL, n INT NOT NULL, valid_n INT NOT NULL, min_v DOUBLE NULL, max_v DOUBLE NULL, avg_v DOUBLE NULL,
  PRIMARY KEY (sensor_id, day));

INSERT INTO reading_daily (sensor_id, day, n, valid_n, min_v, max_v, avg_v)
SELECT sensor_id, DATE(observed_at), COUNT(*), SUM(quality <> 'B'),
       MIN(CASE WHEN quality <> 'B' THEN value END), MAX(CASE WHEN quality <> 'B' THEN value END), AVG(CASE WHEN quality <> 'B' THEN value END)
FROM reading GROUP BY sensor_id, DATE(observed_at)
ON DUPLICATE KEY UPDATE n = VALUES(n), valid_n = VALUES(valid_n), min_v = VALUES(min_v), max_v = VALUES(max_v), avg_v = VALUES(avg_v);

CREATE TABLE IF NOT EXISTS job_run (
  job VARCHAR(40) PRIMARY KEY, last_run_at DATETIME NOT NULL, last_ok TINYINT(1) NOT NULL DEFAULT 1, last_result VARCHAR(500) NULL);
