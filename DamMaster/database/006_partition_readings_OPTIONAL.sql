-- =====================================================================
-- DAM MASTER AI · OPTIONAL — partition the `reading` table by year.
-- When: once readings pass roughly 50–100 million rows, or to make archiving
-- old years cheap (DROP/EXCHANGE PARTITION instead of DELETE).
-- Impact: rebuilds the table (run in a maintenance window; take a backup first).
-- MySQL rule: every unique key must include the partition column, so the
-- primary key becomes (id, observed_at). ux_reading (sensor_id, observed_at)
-- already includes it. No foreign keys exist, so partitioning is allowed.
-- =====================================================================
ALTER TABLE reading DROP PRIMARY KEY, ADD PRIMARY KEY (id, observed_at);

ALTER TABLE reading PARTITION BY RANGE (TO_DAYS(observed_at)) (
  PARTITION p2020 VALUES LESS THAN (TO_DAYS('2021-01-01')),
  PARTITION p2021 VALUES LESS THAN (TO_DAYS('2022-01-01')),
  PARTITION p2022 VALUES LESS THAN (TO_DAYS('2023-01-01')),
  PARTITION p2023 VALUES LESS THAN (TO_DAYS('2024-01-01')),
  PARTITION p2024 VALUES LESS THAN (TO_DAYS('2025-01-01')),
  PARTITION p2025 VALUES LESS THAN (TO_DAYS('2026-01-01')),
  PARTITION p2026 VALUES LESS THAN (TO_DAYS('2027-01-01')),
  PARTITION p2027 VALUES LESS THAN (TO_DAYS('2028-01-01')),
  PARTITION pmax  VALUES LESS THAN MAXVALUE);

-- Yearly maintenance (run every December for the next year), e.g. for 2028:
--   CALL dm_add_reading_partition(2028);
DROP PROCEDURE IF EXISTS dm_add_reading_partition;
DELIMITER $$
CREATE PROCEDURE dm_add_reading_partition(IN y INT)
BEGIN
  SET @s = CONCAT('ALTER TABLE reading REORGANIZE PARTITION pmax INTO (PARTITION p', y,
                  ' VALUES LESS THAN (TO_DAYS(''', y + 1, '-01-01'')), PARTITION pmax VALUES LESS THAN MAXVALUE)');
  PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;
END$$
DELIMITER ;
