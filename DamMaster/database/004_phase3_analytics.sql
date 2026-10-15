-- =====================================================================
-- DAM MASTER AI · Phase 3 — analytics: HST behaviour models, Dam Health
-- Index (components, snapshots), dam documents. MySQL 8. Re-runnable.
-- =====================================================================
SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS hst_model (
  id CHAR(36) PRIMARY KEY, sensor_id CHAR(36) NOT NULL, variant VARCHAR(20) NOT NULL,          -- 'Full HST' or 'Provisional H+T'
  data_from DATETIME NOT NULL, data_to DATETIME NOT NULL, n_points INT NOT NULL,
  h_min DOUBLE NOT NULL, h_max DOUBLE NOT NULL, t0 DATETIME NOT NULL,
  coefficients TEXT NOT NULL,                                                                   -- JSON {name: value}
  r2 DOUBLE NOT NULL, sigma DOUBLE NOT NULL, k_limit DECIMAL(4,2) NOT NULL DEFAULT 2.50, drift_per_year DOUBLE NULL,
  status VARCHAR(20) NOT NULL DEFAULT 'Draft',                                                  -- Draft, Active, Superseded, Rejected
  fitted_by VARCHAR(120), fitted_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  approved_by VARCHAR(120) NULL, approved_at DATETIME NULL, notes VARCHAR(1000) NULL,
  INDEX ix_hst_sensor (sensor_id, status));

CREATE TABLE IF NOT EXISTS ref_dhi_component (
  code VARCHAR(20) PRIMARY KEY, name VARCHAR(80) NOT NULL, weight DECIMAL(4,2) NOT NULL, description VARCHAR(600), sort_order INT DEFAULT 0);

CREATE TABLE IF NOT EXISTS dhi_snapshot (
  id BIGINT AUTO_INCREMENT PRIMARY KEY, dam_id CHAR(36) NOT NULL, computed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  dhi DOUBLE NOT NULL, band VARCHAR(20) NOT NULL, risk INT NOT NULL, rating VARCHAR(20) NOT NULL, components TEXT NOT NULL, computed_by VARCHAR(120) NULL,
  INDEX ix_dhi_dam (dam_id, computed_at));

CREATE TABLE IF NOT EXISTS dam_document (
  id CHAR(36) PRIMARY KEY, dam_id CHAR(36) NOT NULL, doc_type VARCHAR(40) NOT NULL, title VARCHAR(250) NOT NULL, reference_no VARCHAR(100) NULL,
  issue_date DATE NULL, next_review_date DATE NULL, status VARCHAR(20) NOT NULL DEFAULT 'Draft',
  created_by VARCHAR(120), created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at DATETIME NULL,
  INDEX ix_doc_dam (dam_id, doc_type));

DELETE FROM ref_dhi_component;
INSERT INTO ref_dhi_component (code, name, weight, description, sort_order) VALUES
('STRUCTURAL','Structural condition',0.25,'Latest completed inspection condition (Good 100, Fair 80, Poor 55, Unsatisfactory 30; none in 12 months 70); −10 per open High and −20 per open Critical finding (max −40); −5 / −15 per deformation, stress or strain sensor with an open Level 1 / Level 2+ alert (max −30).',1),
('SEEPAGE','Seepage & water pressure',0.20,'100 − 10 / 25 / 40 per seepage or water-pressure sensor with an open Level 1 / 2 / 3 alert (max −60); −15 if a corroboration (failure-mode) alert is open; −10 per open High / Critical finding linked to a seepage or pressure sensor.',2),
('INSTRUMENTATION','Instrumentation',0.15,'0.5 × functional sensors % + 0.3 × data availability % (30 days) + 0.2 × calibration-in-date %.',3),
('HYDROLOGICAL','Hydrological safety',0.15,'100 − 1 per day above FRL in the last 30 days (max −20) − 20 if MWL was exceeded − 20 if a Level 2+ alert is open on a load sensor (reservoir, inflow) − 15 per open High / Critical spillway & gates finding.',4),
('OM','O&M & inspection compliance',0.15,'0.5 × monsoon inspections done on time (of those already due) + 0.5 × findings closed by their due date in the last 365 days.',5),
('DOCUMENTATION','Documentation',0.10,'Approved and in review date: EAP 30, O&M manual 20, instrument register 20, as-built drawings 15, safety review (within 10 years) 15.',6);
