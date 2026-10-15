# Roadmap

| Phase | Content | Status |
|---|---|---|
| **1 — Monitoring core** | Dams, sensors, Excel / CSV import with 8 QC rules, IoT telemetry API, versioned thresholds with approval, PFMA register, 4-level alert engine (threshold, statistical, rate, system, corroboration), reference library, users / roles, audit | **Delivered** |
| **2 — Operations** | Inspections (pre / post-monsoon, special, post-event) with checklist and findings linked to instruments and alerts; monsoon compliance; reading schedules by life phase; in-app / e-mail / SMS notifications with acknowledgement deadlines and escalation; EAP contacts | **Delivered** |
| **3 — Analytics** | HST behaviour models per instrument with approval and residual alerts; Dam Health Index (six explained components, risk, snapshots); reservoir / rainfall response analysis; document register; data availability report | **Delivered** |
| **4 — Live IoT & scale** | MQTT ingestion with acknowledgements, idempotent messages, gateway health and self-resolving data-health alerts, buffered-data reconciliation, daily aggregates, optional partitioning, scheduled HST refits, system status | **Delivered** |
| **5 — Engineering** | Design workspace on the server: nine connected calculations with 12-step records, dependency / STALE engine, versioning, check / approve with segregation of duties, code-verification gating, Excel inputs, validation suite | **Delivered** |
| **6 — Digital twin & remote sensing** | GIS map, live dam section, InSAR and survey campaigns with comparison and alerts, document files with full-text search and cited AI answers, design → threshold suggestions | **Delivered** |
| **7 — Field & scale-out** | Offline field app with photos and GPS, OCR, OpenID Connect SSO, federation of state instances, 3-D dam view | **Delivered** |
| 8 — Hardening & certification | Penetration test and security audit, load test with production-size data, accessibility audit, backup / disaster-recovery drill, user training material, IFC / BIM model import for the 3-D view | Next |

Each phase reuses the Phase 1 foundations: master data, the alert engine, the audit trail and the role / policy model.
