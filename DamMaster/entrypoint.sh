#!/bin/bash
# Entrypoint: wait for MySQL to be ready, run migrations, then start the API.
set -e

# ── Parse DB host/port from the connection string env var ─────────────────────
# Render sets ConnectionStrings__DamMaster via environment variable
CS="${ConnectionStrings__DamMaster:-}"
if [ -z "$CS" ]; then
  echo "ERROR: ConnectionStrings__DamMaster is not set" >&2
  exit 1
fi

# Extract host, port, user, password, database from the connection string
DB_HOST=$(echo "$CS" | grep -oP '(?<=Server=)[^;]+')
DB_PORT=$(echo "$CS" | grep -oP '(?<=Port=)[^;]+')
DB_USER=$(echo "$CS" | grep -oP '(?<=User ID=)[^;]+')
DB_PASS=$(echo "$CS" | grep -oP '(?<=Password=)[^;]+')
DB_NAME=$(echo "$CS" | grep -oP '(?<=Database=)[^;]+')

DB_PORT="${DB_PORT:-3306}"

echo "Waiting for MySQL at $DB_HOST:$DB_PORT ..."
for i in $(seq 1 30); do
  if mysqladmin ping -h "$DB_HOST" -P "$DB_PORT" -u "$DB_USER" --password="$DB_PASS" --silent 2>/dev/null; then
    echo "MySQL is up."
    break
  fi
  echo "  attempt $i/30 — retrying in 3s..."
  sleep 3
done

# ── Run migrations (idempotent SQL scripts) ───────────────────────────────────
echo "Running database migrations..."
MYSQL_CMD="mysql -h $DB_HOST -P $DB_PORT -u $DB_USER --password=$DB_PASS $DB_NAME"

$MYSQL_CMD < database/001_schema.sql
$MYSQL_CMD < database/002_seed_reference.sql
$MYSQL_CMD < database/003_phase2_operations.sql
$MYSQL_CMD < database/004_phase3_analytics.sql
$MYSQL_CMD < database/005_phase4_iot.sql
$MYSQL_CMD < database/007_phase5_design.sql
$MYSQL_CMD < database/008_phase6_twin.sql
$MYSQL_CMD < database/009_phase7_field.sql

echo "Migrations complete. Starting DAM MASTER AI..."

# ── Start the ASP.NET Core API ────────────────────────────────────────────────
exec dotnet DamMaster.Api.dll
