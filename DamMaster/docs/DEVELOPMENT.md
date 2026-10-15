# Development guide

## Conventions
- **C#**: one `Cls_<Aggregate>DAL` per aggregate, inheriting `Cls_DbBase`; instantiate directly (`new Cls_DamDAL()`).
  SQL only in DAL classes. Snake_case columns map to PascalCase properties (`MatchNamesWithUnderscores`).
- **Business rules** throw `BusinessException("plain-English message")` → HTTP 400 shown to the user as-is.
- **Every state change writes an audit entry** (`Audit(...)` in controllers, `Cls_AuditDAL` in engines).
- **IDs**: `Ids.New()` (UUID text). **Times**: UTC everywhere; the UI formats IST.
- **Permissions**: add a policy to `Policies.Map` (server) and the same key to `frontend/src/auth/roles.ts` (UI).
- **TypeScript**: strict mode; API shapes in `src/api/types.ts`; data loading via `useApi` / `useCatalog`.

## Adding a module (example: Inspections, Phase 2)
1. **Schema**: add tables to a new `database/003_inspections.sql` (CHAR(36) keys, indexed link columns, `created_at`).
2. **Model**: classes in `Models.cs` (or a new `Models/Inspections.cs`).
3. **DAL**: `Cls_InspectionDAL : Cls_DbBase` with async Dapper methods.
4. **Controller**: `[Authorize, Route("api/inspections")]`, policy for write actions, `Audit(...)` on changes.
5. **Frontend**: types → page in `src/pages` → route in `App.tsx` → menu link in `Layout.tsx`.
6. **Tests**: extend `tests/integration_test.py`.

## Extending the alert engine
Rules live in `Cls_AlertEngine.EvaluateDamAsync`. Each rule calls `Raise(...)` with a `rule_type`; the engine de-duplicates
open alerts per sensor (or PFMA) and rule type. New rules (e.g. HST residual, rainfall-response) should:
1. use `Cls_Stats` helpers (pure, unit-testable),
2. skip readings flagged B,
3. include the numbers behind the decision in `detail` (value, limit, window, method).

## Local build without internet NuGet access
The project uses NuGet packages `Dapper 2.1.35` and `MySqlConnector 2.4.0`. On machines without nuget.org access,
add an internal feed in `nuget.config` or reference the DLLs directly.
