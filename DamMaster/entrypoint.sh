#!/bin/bash
# Entrypoint: build the connection string from individual Render env vars,
# wait for MySQL to be ready, run migrations, then start the API.
set -e

# ── Validate individual DB env vars injected by Render fromDatabase ───────────
: "${MYSQLHOST:?ERROR: MYSQLHOST env var is not set}"
: "${MYSQLDATABASE:?ERROR: MYSQLDATABASE env var is not set}"
: "${MYSQLUSER:?ERROR: MYSQLUSER env var is not set}"
: "${MYSQLPASSWORD:?ERROR: MYSQLPASSWORD env var is not set}"
MYSQLPORT="${MYSQLPORT:-3306}"

# ── Build the ASP.NET Core connection string and export it ────────────────────
export ConnectionStrings__DamMaster="Server=${MYSQLHOST};Port=${MYSQLPORT};Database=${MYSQLDATABASE};User ID=${MYSQLUSER};Password=${MYSQLPASSWORD};SslMode=Preferred;AllowUserVariables=true;GuidFormat=None"

echo "Waiting for MySQL at ${MYSQLHOST}:${MYSQLPORT} ..."
for i in $(seq 1 30); do
  if mysqladmin ping -h "${MYSQLHOST}" -P "${MYSQLPORT}" -u "${MYSQLUSER}" --password="${MYSQLPASSWORD}" --silent 2>/dev/null; then
    echo "MySQL is up."
    break
  fi
  echo "  attempt ${i}/30 — retrying in 3s..."
  sleep 3
done

# ── Run migrations (idempotent SQL scripts) ───────────────────────────────────
echo "Running database migrations..."
MYSQL_CMD="mysql -h ${MYSQLHOST} -P ${MYSQLPORT} -u ${MYSQLUSER} --password=${MYSQLPASSWORD} ${MYSQLDATABASE}"

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
