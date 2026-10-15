# API reference

Base path `/api`. JSON, camelCase, UTC timestamps (`…Z`). State-changing calls need header `X-Requested-With: DamMaster` (except `/api/telemetry`).
Errors: `{ "error": "message" }` with 400 (business rule), 401, 403, 404 or 500.

| Method | Path | Area | Access |
|---|---|---|---|
| POST | `/api/auth/login` | Auth | Anonymous |
| POST | `/api/auth/logout` | Auth | Signed in |
| GET | `/api/auth/me` | Auth | Signed in |
| POST | `/api/auth/change-password` | Auth | Signed in |
| GET | `/api/reference/{name}` | Reference | Signed in |
| POST | `/api/reference/standards/{code}/verify` | Reference | VerifyStandards |
| DELETE | `/api/reference/standards/{code}/verify` | Reference | VerifyStandards |
| GET | `/api/dams` | Dams | Signed in |
| GET | `/api/dams/{id}` | Dams | Signed in |
| POST | `/api/dams` | Dams | ManageDams |
| PUT | `/api/dams/{id}` | Dams | ManageDams |
| GET | `/api/dams/{id}/sensors` | Dams | Signed in |
| GET | `/api/dams/{id}/pfma` | Dams | Signed in |
| POST | `/api/dams/{id}/evaluate` | Dams | Admin, DamSafetyEngineer, InstrumentationEngineer |
| GET | `/api/sensors/{id}` | Sensors | Signed in |
| POST | `/api/sensors` | Sensors | ManageSensors |
| PUT | `/api/sensors/{id}` | Sensors | ManageSensors |
| GET | `/api/sensors/{id}/readings` | Sensors | Signed in |
| GET | `/api/sensors/{id}/thresholds` | Sensors | Signed in |
| POST | `/api/sensors/{id}/thresholds` | Sensors | ManageSensors |
| POST | `/api/sensors/thresholds/{thresholdId}/approve` | Sensors | ApproveThresholds |
| POST | `/api/pfma` | Pfma | ManagePfma |
| PUT | `/api/pfma/{id}` | Pfma | ManagePfma |
| POST | `/api/import` | Import | ImportData |
| GET | `/api/import/batches` | Import | Signed in |
| GET | `/api/import/{id}/issues` | Import | Signed in |
| GET | `/api/import/quarantine` | Import | Signed in |
| DELETE | `/api/import/quarantine` | Import | ImportData |
| POST | `/api/telemetry` | Telemetry | Gateway API key |
| POST | `/api/gateways` | Gateways | Signed in |
| GET | `/api/alerts` | Alerts | Signed in |
| GET | `/api/alerts/{id}` | Alerts | Signed in |
| POST | `/api/alerts/{id}/acknowledge` | Alerts | AcknowledgeAlerts |
| POST | `/api/alerts/{id}/close` | Alerts | CloseAlerts |
| GET | `/api/dashboard/portfolio` | Dashboard | Signed in |
| GET | `/api/users` | Users | Signed in |
| POST | `/api/users` | Users | Signed in |
| PUT | `/api/users/{id}` | Users | Signed in |
| POST | `/api/users/{id}/reset-password` | Users | Signed in |
| GET | `/api/audit` | Audit | Signed in |
| GET | `/api/inspections` | Inspections | Signed in |
| GET | `/api/inspections/{id}` | Inspections | Signed in |
| POST | `/api/inspections` | Inspections | ManageInspections |
| POST | `/api/inspections/{id}/start` | Inspections | ManageInspections |
| PUT | `/api/inspections/{id}/checks` | Inspections | ManageInspections |
| POST | `/api/inspections/{id}/findings` | Inspections | ManageInspections |
| POST | `/api/inspections/{id}/complete` | Inspections | ManageInspections |
| POST | `/api/inspections/{id}/cancel` | Inspections | ManageInspections |
| GET | `/api/inspections/findings` | Inspections | Signed in |
| POST | `/api/inspections/findings/{id}/status` | Inspections | ManageInspections |
| GET | `/api/inspections/compliance` | Inspections | Signed in |
| GET | `/api/dams/{damId}/eap-contacts` | EapContacts | Signed in |
| POST | `/api/dams/{damId}/eap-contacts` | EapContacts | ManageEap |
| PUT | `/api/dams/{damId}/eap-contacts/{id}` | EapContacts | ManageEap |
| GET | `/api/notifications/mine` | Notifications | Signed in |
| GET | `/api/notifications/mine/count` | Notifications | Signed in |
| POST | `/api/notifications/mine/read` | Notifications | Signed in |
| GET | `/api/notifications/log` | Notifications | ViewNotificationLog |
| POST | `/api/notifications/run` | Notifications | Admin |
| GET | `/api/dams/{damId}/schedule` | Schedule | Signed in |
| GET | `/api/alerts/{id}/notifications` | AlertExtras | Signed in |
| POST | `/api/alerts/{id}/inspection` | AlertExtras | ManageInspections |
| GET | `/api/sensors/{sensorId}/hst` | Hst | Signed in |
| POST | `/api/sensors/{sensorId}/hst/fit` | Hst | FitModels |
| POST | `/api/sensors/{sensorId}/hst/{modelId}/approve` | Hst | ApproveThresholds |
| POST | `/api/sensors/{sensorId}/hst/{modelId}/reject` | Hst | ApproveThresholds |
| GET | `/api/sensors/{sensorId}/response` | Response | Signed in |
| GET | `/api/dams/{damId}/dhi` | Dhi | Signed in |
| POST | `/api/dams/{damId}/dhi/snapshot` | Dhi | Admin, DamSafetyEngineer |
| GET | `/api/dams/{damId}/documents` | Documents | Signed in |
| POST | `/api/dams/{damId}/documents` | Documents | ManageDocuments |
| PUT | `/api/dams/{damId}/documents/{id}` | Documents | ManageDocuments |
| GET | `/api/reports/availability` | Reports | Signed in |
| GET | `/api/gateways` | GatewayHealth | Signed in |
| GET | `/api/gateways/{id}` | GatewayHealth | Signed in |
| PUT | `/api/gateways/{id}` | GatewayHealth | ManageGateways |
| POST | `/api/gateways/{id}/rotate-key` | GatewayHealth | ManageGateways |
| GET | `/api/gateways/{id}/reconciliation` | GatewayHealth | Signed in |
| GET | `/api/system/status` | System | Signed in |
| POST | `/api/system/run-jobs` | System | Admin |
| GET | `/api/design/catalog` | Design | Signed in |
| GET | `/api/design/projects` | Design | Signed in |
| POST | `/api/design/projects` | Design | EditDesign |
| GET | `/api/design/projects/{id}` | Design | Signed in |
| PUT | `/api/design/projects/{id}` | Design | EditDesign |
| PUT | `/api/design/projects/{id}/inputs` | Design | EditDesign |
| PUT | `/api/design/projects/{id}/ea` | Design | EditDesign |
| POST | `/api/design/projects/{id}/pull-dam` | Design | EditDesign |
| POST | `/api/design/projects/{id}/run` | Design | EditDesign |
| GET | `/api/design/projects/{id}/calcs/{code}` | Design | Signed in |
| POST | `/api/design/projects/{id}/calcs/{code}/check` | Design | CheckDesign |
| POST | `/api/design/projects/{id}/calcs/{code}/approve` | Design | ApproveDesign |
| GET | `/api/design/projects/{id}/impact` | Design | Signed in |
| GET | `/api/design/projects/{id}/changes` | Design | Signed in |
| GET | `/api/design/validation` | Design | Signed in |
| GET | `/api/map` | Map | Signed in |
| GET | `/api/surveys` | Surveys | Signed in |
| GET | `/api/surveys/{id}` | Surveys | Signed in |
| POST | `/api/surveys` | Surveys | ManageSurveys |
| GET | `/api/surveys/compare` | Surveys | Signed in |
| GET | `/api/dams/{damId}/documents/{documentId}/files` | DocumentFiles | Signed in |
| POST | `/api/dams/{damId}/documents/{documentId}/files` | DocumentFiles | ManageDocuments |
| GET | `/api/files/{id}` | Files | Signed in |
| GET | `/api/library/search` | Library | Signed in |
| POST | `/api/library/ask` | Library | Signed in |
| GET | `/api/dams/{damId}/twin` | Twin | Signed in |
| GET | `/api/dams/{damId}/design-thresholds` | Twin | Signed in |
| POST | `/api/dams/{damId}/design-thresholds` | Twin | ApplyDesignThresholds |
| GET | `/api/field/inspections` | Field | Signed in |
| GET | `/api/field/package/{inspectionId}` | Field | Signed in |
| POST | `/api/field/sync` | Field | ManageInspections |
| GET | `/api/findings/{findingId}/photos` | Photos | Signed in |
| POST | `/api/findings/{findingRef}/photos` | Photos | ManageInspections |
| GET | `/api/photos/{id}` | Photos | Signed in |
| GET | `/api/auth/sso/config` | Sso | Anonymous |
| GET | `/api/auth/sso/login` | Sso | Anonymous |
| GET | `/api/auth/sso/callback` | Sso | Anonymous |
| GET | `/api/federation/summary` | Federation | Federation key |
| GET | `/api/federation/portfolio` | Federation | Admin, Management, Approver, DamSafetyEngineer |

Class-level rules also apply: `/api/users/*` needs **ManageUsers**, creating gateways needs **ManageGateways**,
`/api/audit` needs Admin, Approver, DamSafetyEngineer or Management. Closing a finding additionally needs **CloseFindings** (Dam Safety Engineer). Policy → roles mapping: `Common.cs → Policies.Map`.

## Import payload (`POST /api/import`)
```json
{ "fileName": "file.xlsx", "commit": false,
  "dams":     [{ "damId": "DM-001", "damName": "River Valley Dam", "damType": "Concrete gravity", "hazardClass": "High", "frlM": 815, "mwlM": 818 }],
  "sensors":  [{ "sensorId": "DM001-PZ-07", "damId": "DM-001", "parameterCode": "UPL", "engUnit": "kPa", "expectedMin": 0, "expectedMax": 400,
                 "alertLevel": 215, "alarmLevel": 235, "emergencyLevel": 260, "direction": "Rising", "readingIntervalMin": 360 }],
  "readings": [{ "damId": "DM-001", "sensorId": "DM001-PZ-07", "parameterCode": "UPL", "observedAt": "2026-10-04T18:00:00+05:30", "value": 198.4, "unit": "kPa", "loggerStatus": "OK" }] }
```
Times without an offset are read as IST. Response: counts (`good`, `suspect`, `bad`, `rejected`, `quarantined`, `duplicates`), `issues[]`, and on commit `alertsRaised[]`.

## Telemetry (`POST /api/telemetry`)
Headers `X-Gateway-Code`, `X-Api-Key` (from `POST /api/gateways`, shown once).
```json
{ "schemaVersion": "1.0", "messageId": "uuid", "gatewayId": "GW-DM001-01", "damId": "DM-001",
  "readings": [{ "sensorId": "DM001-PZ-07", "parameterCode": "UPL", "observedAt": "2026-10-05T06:00:00+05:30", "value": 237.5, "unit": "kPa", "loggerStatus": "OK" }] }
```
Returns 202 with accepted / rejected / quarantined / duplicate counts. The same sensor and time is never stored twice, so retries are safe.

## Reference catalogues (`GET /api/reference/{name}`)
`dam-types`, `dam-functions`, `hazard-classes`, `failure-modes`, `parameter-groups`, `parameters`, `instrument-types`,
`failure-mode-parameters`, `standards`, `alert-levels`, `interpretations`.

## MQTT
Topics, payloads, acknowledgements and buffering rules: see `IOT_GATEWAY_GUIDE.md`.
