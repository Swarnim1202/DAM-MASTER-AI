-- =====================================================================
-- DAM MASTER AI · Phase 5 — design workspace (projects, inputs, nine connected
-- calculations with records, dependency STALE flags, check / approve). MySQL 8.
-- =====================================================================
SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS ref_design_input (
  input_key VARCHAR(20) PRIMARY KEY, label VARCHAR(120) NOT NULL, unit VARCHAR(20) NULL, grp VARCHAR(40) NOT NULL, kind VARCHAR(10) NOT NULL,
  default_json VARCHAR(200) NOT NULL, options_json VARCHAR(600) NULL, help VARCHAR(600) NULL, sort_order INT NOT NULL);

CREATE TABLE IF NOT EXISTS design_project (
  id CHAR(36) PRIMARY KEY, code VARCHAR(30) NOT NULL, name VARCHAR(200) NOT NULL, dam_id CHAR(36) NULL, status VARCHAR(20) NOT NULL DEFAULT 'Active',
  inputs_json TEXT NOT NULL, ea_json TEXT NOT NULL, created_by VARCHAR(120), created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at DATETIME NULL,
  UNIQUE KEY ux_dp_code (code), INDEX ix_dp_dam (dam_id));

-- current version of each calculation per project
CREATE TABLE IF NOT EXISTS design_calc (
  project_id CHAR(36) NOT NULL, calc_code VARCHAR(10) NOT NULL, version INT NOT NULL, status VARCHAR(20) NOT NULL DEFAULT 'Draft',
  stale TINYINT(1) NOT NULL DEFAULT 0, stale_key VARCHAR(20) NULL, stale_reason VARCHAR(300) NULL, record_json MEDIUMTEXT NOT NULL, inputs_hash CHAR(64) NOT NULL,
  run_by VARCHAR(120) NOT NULL, run_at DATETIME NOT NULL, checked_by VARCHAR(120) NULL, checked_at DATETIME NULL, approved_by VARCHAR(120) NULL, approved_at DATETIME NULL,
  maturity VARCHAR(20) NOT NULL DEFAULT 'Preliminary', PRIMARY KEY (project_id, calc_code));

-- every version ever produced (audit / reproducibility)
CREATE TABLE IF NOT EXISTS design_calc_history (
  id BIGINT AUTO_INCREMENT PRIMARY KEY, project_id CHAR(36) NOT NULL, calc_code VARCHAR(10) NOT NULL, version INT NOT NULL, record_json MEDIUMTEXT NOT NULL,
  inputs_json TEXT NOT NULL, run_by VARCHAR(120) NOT NULL, run_at DATETIME NOT NULL, INDEX ix_dch (project_id, calc_code, version));

CREATE TABLE IF NOT EXISTS design_input_change (
  id BIGINT AUTO_INCREMENT PRIMARY KEY, project_id CHAR(36) NOT NULL, input_key VARCHAR(20) NOT NULL, old_value VARCHAR(400) NULL, new_value VARCHAR(400) NULL,
  affected VARCHAR(300) NULL, changed_by VARCHAR(120) NOT NULL, changed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, INDEX ix_dic (project_id, changed_at));

DELETE FROM ref_design_input;
INSERT INTO ref_design_input (input_key, label, unit, grp, kind, default_json, options_json, help, sort_order) VALUES
('name','Project name','','Project','text','"Integrated dam project \\u2014 preliminary"',NULL,'Free text shown on reports.',1),
('rbl','River bed level','m','Site & foundation','number','600',NULL,'Lowest natural bed level at the dam axis, m above MSL (survey).',2),
('fdn','Foundation depth below bed','m','Site & foundation','number','8',NULL,'Depth of stripping / excavation to sound foundation (geology). Foundation level = river bed − this depth.',3),
('len','Valley width at crest','m','Site & foundation','number','480',NULL,'Width of the valley at top-of-dam level (topographic survey).',4),
('valley','Valley shape','','Site & foundation','select','"U-shaped"','["V-shaped (narrow)", "U-shaped", "Wide / open"]','Used for the volume shape factor (V 0.45, U 0.62, wide 0.80) and type scoring.',5),
('found','Foundation','','Site & foundation','select','"Excellent rock"','["Excellent rock", "Good rock", "Fair rock", "Soil / alluvium"]','Foundation class from the geological investigation.',6),
('zone','Seismic zone (IS 1893)','','Site & foundation','select','"III"','["II", "III", "IV", "V"]','Seismic zone of the site; gives the placeholder seismic coefficient αh = (Z/2)·1.5.',7),
('type','Dam type','','Site & foundation','select','"Auto"','["Auto", "Concrete gravity", "Earthfill (zoned)", "Earthfill (homogeneous)", "Rockfill (CFRD)"]','Auto = use the AI-recommended type; or force a type.',8),
('frl','FRL','m','Reservoir & hydrology','number','670',NULL,'Full reservoir level (normal maximum operating level).',9),
('mwl','MWL','m','Reservoir & hydrology','number','673',NULL,'Maximum water level during the inflow design flood (must be ≥ FRL).',10),
('mddl','MDDL','m','Reservoir & hydrology','number','640',NULL,'Minimum draw-down level (lowest operating level).',11),
('twln','Tailwater, normal','m','Reservoir & hydrology','number','602',NULL,'Downstream water level at normal power / release flow.',12),
('twlf','Tailwater at design flood','m','Reservoir & hydrology','number','612',NULL,'Downstream level at the spillway design outflow (river rating curve).',13),
('Q','Spillway design outflow','m³/s','Reservoir & hydrology','number','12000',NULL,'Outflow the spillway is sized for at MWL. Check it against the routed peak outflow.',14),
('Qp','Inflow flood peak','m³/s','Reservoir & hydrology','number','15000',NULL,'Peak of the inflow design flood hydrograph (hydrology study; PMF / SPF as applicable).',15),
('tp','Time to peak','h','Reservoir & hydrology','number','12',NULL,'Time from start of flood to inflow peak (triangular hydrograph).',16),
('tb','Flood base time','h','Reservoir & hydrology','number','36',NULL,'Total duration of the inflow flood (must exceed time to peak).',17),
('wind','Wind velocity','km/h','Reservoir & hydrology','number','120',NULL,'Design wind speed over the reservoir for wave height.',18),
('fetch','Fetch','km','Reservoir & hydrology','number','8',NULL,'Longest open-water distance over which wind blows towards the dam.',19),
('gc','Concrete unit weight','kN/m³','Materials','number','24',NULL,'Typical 23.5–24.5 kN/m³ for mass concrete.',20),
('mu','Concrete–rock friction μ','','Materials','number','0.75',NULL,'tan φ at the dam–rock contact, from shear tests (design value).',21),
('c','Concrete–rock cohesion','kPa','Materials','number','400',NULL,'Cohesion at the contact, from in-situ shear tests (design value). Strongly affects sliding.',22),
('up','Uplift factor (drains working)','','Materials','number','0.67',NULL,'1.0 = no drainage; lower values assume effective foundation drains.',23),
('phi','Earth shell friction angle φ','°','Materials','number','34',NULL,'Effective friction angle of the embankment shell material (lab tests).',24),
('spc','Spillway crest level','m','Spillway & gates','number','658',NULL,'Ogee crest level (below FRL; gates hold water above it).',25),
('bay','Gate bay width','m','Spillway & gates','number','15',NULL,'Clear width of one spillway gate.',26),
('pier','Pier width','m','Spillway & gates','number','3',NULL,'Width of each intermediate pier.',27),
('Cd','Ogee discharge coefficient C','','Spillway & gates','number','2.2',NULL,'SI coefficient in Q = C·L·H^1.5 (typically 2.0–2.2 for a design-head ogee).',28),
('gateW','Gate weight per gate (assumed)','kN','Spillway & gates','number','1100',NULL,'Placeholder until the structural gate design gives the actual weight.',29),
('hoistV','Hoisting speed','m/min','Spillway & gates','number','0.3',NULL,'Gate lifting speed (typical 0.3–0.5 m/min).',30),
('hoistEta','Hoist mechanical efficiency','','Spillway & gates','number','0.85',NULL,'Overall efficiency of gearbox, drum and sheaves.',31),
('hp','Hydropower included','','Hydropower','select','"Yes"','["Yes", "No"]','Switch off for non-power projects.',32),
('Qd','Design discharge','m³/s','Hydropower','number','120',NULL,'Total turbine design discharge.',33),
('D','Penstock diameter','m','Hydropower','number','5.5',NULL,'Internal diameter of the penstock.',34),
('Lp','Penstock length','m','Hydropower','number','250',NULL,'Length from intake to turbine.',35),
('f','Darcy friction factor','','Hydropower','number','0.012',NULL,'Typical 0.010–0.015 for welded steel.',36),
('eta','Overall efficiency (turbine × generator)','','Hydropower','number','0.9',NULL,'Typical 0.88–0.92 at rated load.',37),
('aw','Pressure wave speed','m/s','Hydropower','number','1000',NULL,'Typical 900–1 200 m/s for steel penstocks.',38),
('Tc','Valve / guide-vane closure time','s','Hydropower','number','10',NULL,'Effective closure time used for water hammer.',39),
('sig','Allowable steel stress (assumed)','MPa','Hydropower','number','150',NULL,'From steel grade and the penstock code; confirm.',40),
('rConc','Concrete rate','₹/m³','Rates & cost','number','7500',NULL,'From the applicable Schedule of Rates.',41),
('rFill','Earthfill rate','₹/m³','Rates & cost','number','650',NULL,'From the applicable Schedule of Rates.',42),
('rRock','Rockfill rate','₹/m³','Rates & cost','number','900',NULL,'From the applicable Schedule of Rates.',43),
('rSteel','Penstock steel, fabricated & erected','₹/t','Rates & cost','number','140000',NULL,'Supply, fabrication and erection.',44),
('rGate','Gate & hoist steel, supplied & erected','₹/t','Rates & cost','number','250000',NULL,'Supply, fabrication and erection.',45),
('rEM','E&M equipment (parametric)','₹ cr/MW','Rates & cost','number','5',NULL,'Placeholder until OEM budget quotes.',46),
('ind','Indirect costs','%','Rates & cost','number','12',NULL,'Engineering, supervision, establishment.',47),
('cont','Contingency','%','Rates & cost','number','10',NULL,'Unforeseen items.',48),
('esc','Escalation','%','Rates & cost','number','5',NULL,'Price escalation to completion.',49),
('tax','Taxes (as applicable — confirm)','%','Rates & cost','number','12',NULL,'Applicable taxes on works; confirm current rates.',50),
('slN','Sliding / slope FS, normal (min)','','Design criteria','number','1.5',NULL,'Screening minimum factor of safety for the normal load case (sliding of concrete dams, steady-seepage slope of embankments). Set to the governing standard.',51),
('slS','Sliding FS, seismic (min)','','Design criteria','number','1.2',NULL,'Screening minimum for the normal + earthquake case of concrete dams.',52),
('ot','Overturning FS (min)','','Design criteria','number','1.5',NULL,'Screening minimum against overturning, normal case.',53);

DELETE FROM ref_design_input WHERE input_key='name';   -- the project name lives on design_project

-- default elevation–area table used for new projects (km²)
CREATE TABLE IF NOT EXISTS ref_design_setting (setting_key VARCHAR(40) PRIMARY KEY, value_json TEXT NOT NULL);
INSERT INTO ref_design_setting (setting_key, value_json) VALUES ('default_ea', '[[640, 18], [650, 24], [660, 31], [670, 39], [675, 43], [680, 47]]') ON DUPLICATE KEY UPDATE value_json=VALUES(value_json);

-- standards cited by the design calculations that were not in the Phase 1 register
INSERT INTO ref_standard (code, title, subject, category, sort_order) VALUES
('USBR_DSD','USBR — Design of Small Dams','Dam type selection and design practice','International',40),
('USBR_EM25','USBR Engineering Monograph 25','Hydraulic design of stilling basins and energy dissipators','International',41),
('CWC_DF','CWC guidelines on design flood for dams','Inflow design flood and routing','CWC / DRIP',42),
('SOR','Applicable Schedule of Rates (state / CPWD / project)','Unit rates for cost estimates','Cost',43),
('IS11639','IS 11639','Structural design of penstocks','Hydraulic structures',44),
('IS7396','IS 7396','Hydraulic design of surge tanks','Hydraulic structures',45)
ON DUPLICATE KEY UPDATE title=VALUES(title), subject=VALUES(subject), category=VALUES(category);
