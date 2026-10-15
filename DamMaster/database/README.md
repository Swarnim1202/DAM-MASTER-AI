# Database scripts (MySQL 8)
Run in order:
1. `001_schema.sql` — all tables (idempotent: `CREATE TABLE IF NOT EXISTS`).
2. `002_seed_reference.sql` — reference catalogues from RCPL-DMAI-REF-001 (re-runnable; standard verifications are preserved).

`gen_seed.py` regenerates `002_seed_reference.sql` from the Python lists (edit there, then run `python gen_seed.py`).
Future changes: add numbered scripts (`003_...sql`) — never edit a script already applied in production.
