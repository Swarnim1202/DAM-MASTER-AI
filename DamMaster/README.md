# DAM MASTER AI — Dam Safety Monitoring System (Phase 7)

React 18 + TypeScript frontend · ASP.NET Core 8 Web API (C#, Dapper) · MySQL 8.

**Phase 1** — dam and sensor master data, Excel / CSV import with 8 validation rules, IoT gateway telemetry API,
versioned thresholds with approval, PFMA (failure mode) register, four-level alert engine, reference library,
users and roles, full audit trail.

**Phase 2** — inspections (pre- / post-monsoon, special, post-event) with a standard checklist and findings linked to
instruments; High / Critical findings raise alerts; every Level 2+ instrument alert creates a special inspection task;
Dam Safety Act monsoon-inspection compliance; reading schedules by life phase with compliance %; EAP contacts;
in-app, e-mail and SMS notifications with acknowledgement deadlines and automatic escalation.

**Phase 3** — analytics: HST behaviour models per instrument (fit → approve → residual alerts, replacing the 14-day
statistical band); reservoir and rainfall response analysis; Dam Health Index with six explained components, risk score,
daily snapshots and history; document register; data availability report (CSV).

**Phase 4** — live IoT: MQTT ingestion (QoS 1, persistent session, acknowledgements), idempotent messages over MQTT
and HTTPS, gateway health dashboard (online / late / offline, battery, signal, latency) with self-resolving alerts,
buffered-data reconciliation, daily aggregates for long charts, optional yearly partitioning of readings, scheduled
HST refits, system status page. See `docs/IOT_GATEWAY_GUIDE.md`.

**Phase 5** — design workspace: design projects with 52 inputs and a reservoir elevation–area table; nine connected
calculations on the server (top of dam, dam type selection, main dam stability, spillway, flood routing, stilling
basin, radial gate & hoist, hydropower & penstock, quantities & cost) with 12-step calculation records; dependency
engine flags dependent calculations STALE on any input change; versioned runs; check (Checker / Dam Safety Engineer)
and approve (Approver) with segregation of duties and code-verification gating; Excel input import / export; pull
levels from a registered dam; engine validation suite (26 reference cases). New roles: Design Engineer, Checker.

**Phase 6** — digital twin and remote sensing: GIS map (dams by alert level, located instruments, InSAR points);
live dam section with instruments at their real positions and current reservoir / tailwater; InSAR velocity datasets
(Level 1 alert above threshold, split between structure and rim) and survey campaigns compared epoch to epoch;
document files (PDF, Word, text) with full-text search and "Ask" answers from an optional self-hosted AI model that
must cite the retrieved passages; design → monitoring link (threshold suggestions from an approved design: uplift
from the design uplift diagram at each piezometer, reservoir level from FRL / MWL).

**Phase 7** — field and scale-out: mobile field app (`/field`, installable, works offline: download an inspection,
record checklist, findings and photos with GPS without signal, sync later — every queued action has its own id so
retries and two devices never create duplicates); photos on findings; OCR for scanned PDFs and images (Tesseract);
single sign-on with OpenID Connect (Keycloak, Entra ID / Azure AD, ADFS) with PKCE and signed-token checks; federation
— each state runs its own instance and a central hub reads only their portfolio summaries; 3-D dam view.

```
DamMaster/
├── database/        001 → 005, 007 → 009 SQL scripts (run in order); 006 optional partitioning
├── backend/         DamMaster.sln → DamMaster.Api (Controllers, DAL, Engine, Models, Common)
├── frontend/        React app (Vite). Builds into backend/DamMaster.Api/wwwroot
├── samples/         Excel import template + 90-day sample workbook
├── tests/           integration tests for phases 1–7 + load_test.py
└── docs/            ARCHITECTURE.md, API.md, DEVELOPMENT.md, ROADMAP.md
```

## 1. Prerequisites
| Tool | Version |
|---|---|
| MySQL Server | 8.0 or later |
| .NET SDK | 8.0 |
| Node.js | 18 or later (20 LTS recommended) |
| Python 3 + `requests`, `openpyxl` | only for the integration tests |

## 2. Database
```sql
CREATE DATABASE dammaster CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE USER 'dammaster'@'localhost' IDENTIFIED BY '<strong password>';
GRANT ALL ON dammaster.* TO 'dammaster'@'localhost';
```
```bash
mysql -u dammaster -p dammaster < database/001_schema.sql
mysql -u dammaster -p dammaster < database/002_seed_reference.sql
mysql -u dammaster -p dammaster < database/003_phase2_operations.sql
mysql -u dammaster -p dammaster < database/004_phase3_analytics.sql
mysql -u dammaster -p dammaster < database/005_phase4_iot.sql
mysql -u dammaster -p dammaster < database/007_phase5_design.sql
mysql -u dammaster -p dammaster < database/008_phase6_twin.sql
mysql -u dammaster -p dammaster < database/009_phase7_field.sql
# optional, for very large installations (rebuilds the readings table — maintenance window):
# mysql -u dammaster -p dammaster < database/006_partition_readings_OPTIONAL.sql
```
All scripts are re-runnable. The seed keeps any standard verifications already recorded.
Upgrading: run every script newer than your current phase (from Phase 6: only `009_phase7_field.sql`).

## 3. Backend
Edit `backend/DamMaster.Api/appsettings.json` (or create `appsettings.Development.json`):
```json
"ConnectionStrings": { "DamMaster": "Server=localhost;Port=3306;Database=dammaster;User ID=dammaster;Password=...;SslMode=Preferred;AllowUserVariables=true;GuidFormat=None" }
```
> `GuidFormat=None` is required — keys are stored as CHAR(36) text.

```bash
cd backend
dotnet restore
dotnet run --project DamMaster.Api --urls http://localhost:5080
```
On first start, if the user table is empty, an administrator is created from `Bootstrap` settings
(default `admin` / `ChangeMe@2026`). The system forces a password change at first sign-in. Change the
bootstrap password in configuration before deploying.

### Notifications and inspections (Phase 2 settings)
```json
"Notifications": {
  "AppBaseUrl": "https://dammaster.yourdomain.gov.in",
  "EscalationIntervalSeconds": 60, "MaxEscalationSteps": 4,
  "Smtp": { "Host": "smtp.yourdomain.gov.in", "Port": 587, "User": "...", "Password": "...", "From": "dammaster@yourdomain.gov.in", "EnableSsl": true },
  "Sms":  { "UrlTemplate": "https://<sms-gateway>/send?to={to}&text={message}&key=<KEY>" }
},
"Inspection": { "PreMonsoonDue": "05-31", "PostMonsoonDue": "11-30" }
```
Leave `Smtp:Host` / `Sms:UrlTemplate` empty to run without sending — messages are recorded as **Simulated** in the
Notifications log. Acknowledgement deadlines per level come from table `ref_alert_level.ack_minutes` (L1 24 h, L2 2 h,
L3 15 min — confirm against the dam's EAP). Monsoon due dates must be confirmed with the State Dam Safety Organisation.

### Live IoT (Phase 4 settings)
```json
"Mqtt": { "Enabled": true, "Host": "broker.yourdomain.gov.in", "Port": 8883, "UseTls": true,
          "Username": "dammaster-api", "Password": "...", "ClientId": "dammaster-api", "TopicRoot": "dammaster" },
"Hst": { "RefitDays": 30 },
"Gateway": { "LowBatteryVolts": 11.5 }
```
With `Mqtt:Enabled=false` gateways can still post to `/api/telemetry`. Broker set-up and ACLs: `docs/IOT_GATEWAY_GUIDE.md`.

### Files, AI and map (Phase 6 settings)
```json
"Storage": { "Root": "D:/DamMaster/files", "MaxUploadMb": 50 },
"Ai":  { "Endpoint": "http://ai-server:8080/v1/chat/completions", "Model": "your-model", "ApiKey": "" },
"Map": { "TileUrl": "https://tile.openstreetmap.org/{z}/{x}/{y}.png", "Attribution": "© OpenStreetMap contributors", "StructureRadiusM": 1000 }
```
- `Storage:Root` must be writable by the app pool and included in backups (uploaded documents live there).
- `Ai:Endpoint` is any OpenAI-compatible chat endpoint — use a self-hosted model to keep documents inside your network.
  Empty = search and "Ask" still work, returning the best passages without a generated answer.
- `Map:TileUrl` should point to an internal tile server on closed networks.

### Field app, OCR, SSO and federation (Phase 7 settings)
```json
"Ocr": { "Enabled": true, "TesseractPath": "C:/Program Files/Tesseract-OCR/tesseract.exe", "PdftoppmPath": "C:/poppler/bin/pdftoppm.exe", "Language": "eng", "Dpi": 200 },
"Sso": { "Enabled": true, "Authority": "https://login.yourdomain.gov.in/realms/dams", "ClientId": "dammaster", "ClientSecret": "...", "UserClaim": "email", "AllowLocalLogin": true },
"Federation": { "InstanceName": "Maharashtra", "InstanceKey": "<long random key>", "IncludeSelf": true,
                "Instances": [ { "Name": "Kerala", "Url": "https://dams.kerala.example", "Key": "<their key>" } ] }
```
- **HTTPS is required** for the field app on phones (camera, GPS, offline service worker all need a secure origin).
  Inspectors open `https://<site>/field`, sign in once with signal, download inspections, then work offline.
- **OCR**: install Tesseract OCR and Poppler (for `pdftoppm`) on the server; add `-l eng+hin` style languages as needed.
- **SSO**: register a confidential client at the identity provider with redirect URI `https://<site>/api/auth/sso/callback`.
  Accounts must already exist in DAM MASTER (matched by e-mail; the first sign-in links the provider's subject id);
  roles stay in DAM MASTER. With `AllowLocalLogin=false` only Administrators may use passwords (break-glass).
- **Federation**: give each state instance a long random `InstanceKey`; the hub lists them under `Instances`.
  Only summaries (DHI, risk, alert counts) cross instances — readings and documents stay with the owner.
- Data-protection keys are stored in `<Storage:Root>/keys` so sessions survive restarts; back this folder up.

## 4. Frontend
```bash
cd frontend
npm install
npm run dev        # http://localhost:5173 — proxies /api to http://localhost:5080
npm run build      # production build into backend/DamMaster.Api/wwwroot
```
After `npm run build`, the API serves the app itself: open http://localhost:5080.

## 5. First run — 5 minutes
1. Sign in as `admin`, set a new password.
2. **Users & roles** → create an Instrumentation Engineer, a Dam Safety Engineer and an Approver.
3. Sign in as the Instrumentation Engineer → **Import data** → choose `samples/DAM_MASTER_IoT_Sample_RiverValley_90days.xlsx`
   → review the validation (7 seeded errors are caught) → **Commit import**.
4. Sign in as the Dam Safety Engineer → **Dams → River Valley Dam → Failure modes** → add *Internal erosion / piping*
   and link the seepage, turbidity and uplift sensors.
5. Open a sensor → **New version** of thresholds → sign in as the Approver to approve it → **Re-evaluate alerts**.
6. **Dam → EAP contacts**: add the evacuation authority (order 1) and the regulator (order 2).
7. Create an Inspector user → **Inspections → Plan inspection** → Start → record the checklist → record findings → Complete.
8. **Notifications** (Admin / Dam Safety Engineer) shows every message; the bell shows in-app alerts.
9. **Dam → Health (DHI)** shows the index, risk and the reason behind every component score; register documents under **Documents**.
10. Open a seepage or pressure sensor → **Behaviour model (HST)** → *Fit new model* (Instrumentation / Dam Safety Engineer)
    → another engineer approves it. With under ~11 months of data a provisional H+T model is fitted.
11. **Data availability** (menu) — expected vs received readings per sensor, CSV export.
12. **Gateways** → register a gateway → configure the logger with the topics shown → watch state, battery and the message log.
13. **System status** shows the MQTT connection, background jobs and storage; Admin can run all jobs now.
14. Create a Design Engineer and a Checker → **Design workspace → New design project** → change an input (e.g. FRL)
    → see the STALE list and dependency paths → **Recalculate STALE** → Checker marks a calculation checked → Approver
    verifies its standards (Reference library → Codes & standards) and approves. **Design validation** runs the engine tests.
15. Set coordinates, offset from heel and chainage on sensors → **Map** and **Dam → Live section**.
16. **Remote sensing & surveys** → import an InSAR CSV (`point_id, latitude, longitude, velocity_mm_yr`) or two survey
    campaigns (`point_id, latitude, longitude, elevation_m`) and compare them.
17. **Dam → Documents** → upload the files (e.g. `samples/Dam_Safety_Monitoring_Expert_Reference.pdf`) → **Document search**.
18. **Dam → Design link** → suggestions from the approved design project → create threshold versions → approve them.
19. On a phone: `/field` → Download an inspection → switch to flight mode → Start, checklist, finding with photo, Complete
    → switch flight mode off → it syncs by itself (or tap **Sync now**). Desk view shows the finding with photo and GPS.
20. **Federated portfolio** (Management, Approver, Dam Safety Engineer, Admin) and **Dam → 3-D view**.

## 6. Tests
```bash
pip install requests openpyxl
# start the API on an EMPTY database (all three scripts applied), then:
DM_API=http://localhost:5080 python tests/integration_test.py
DM_API=http://localhost:5080 DM_MYSQL_CMD="mysql -u root dammaster" python tests/integration_test_phase2.py
DM_API=http://localhost:5080 python tests/integration_test_phase3.py
# Phase 4: start a local broker (mosquitto) and the API with Mqtt__Enabled=true, then:
pip install paho-mqtt
DM_API=http://localhost:5080 DM_MYSQL_CMD="mysql -u root dammaster" python tests/integration_test_phase4.py
DM_API=http://localhost:5080 python tests/integration_test_phase5.py
# Phase 6: start the API with Ai__Endpoint=http://127.0.0.1:5099/v1/chat/completions (the test runs a mock model there)
DM_API=http://localhost:5080 python tests/integration_test_phase6.py
# Phase 7: main API with Ocr__Enabled=true, Sso__Enabled=true, Sso__Authority=http://127.0.0.1:5098, Sso__ClientId=dammaster,
# Sso__ClientSecret=secret and Federation__Instances__0 pointing to a second instance (DM_API2) — the test runs a mock identity provider
DM_API=http://localhost:5080 DM_API2=http://localhost:5081 python tests/integration_test_phase7.py
DM_API=http://localhost:5080 python tests/load_test.py      # 100 000 readings in one import
```
The Phase 3 test builds a synthetic two-year dataset with a known behaviour (water load, season, 1.5 kPa/year drift and
0.4 kPa noise) and checks the HST fit recovers it (R² > 0.97, σ ≈ 0.4, drift ≈ 1.5/year) before testing residual alerts.
The first script signs in as the bootstrap admin with `ChangeMe@2026`, so run on a fresh database. `DM_MYSQL_CMD` is optional:
it lets the Phase 2 test move an acknowledgement deadline into the past to verify escalation.

## 7. Deploying on IIS (Windows Server)
1. Install the **ASP.NET Core 8 Hosting Bundle**.
2. `cd frontend && npm ci && npm run build`
3. `cd backend && dotnet publish DamMaster.Api -c Release -o C:\inetpub\dammaster`
4. Create an IIS site pointing to that folder (App pool: *No Managed Code*). Bind HTTPS.
5. Put the production connection string in environment variable `ConnectionStrings__DamMaster` or in `appsettings.Production.json`.
6. Behind HTTPS the session cookie is sent as Secure automatically.

See `docs/` for architecture, API reference, coding conventions and the roadmap.
