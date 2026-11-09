# Compliance Evidence — Phase-01 Slice D2

**Date:** 2026-05-01
**Plan:** `plans/260428-1740-cap-4-qd-742-compliance/phase-01-audit-log-baseline.md`
**Slice:** D2 (M2 trigger + RetentionPolicy CRUD + AuditLogPurgeJob)
**ReqIDs covered:** QĐ 742 §3.2.a (UI policy mgmt — BE only here), §3.2.b (retention config), §3.2 (immutability)

## Acceptance Matrix

| Req | Description | Evidence | Status |
|-----|-------------|----------|--------|
| §3.2.a | API quản lý chính sách nhật ký | `AuditRetentionPoliciesController` GET/POST/PUT/DELETE — `tests/integration/AqtCoreFW.Audit.Integration.Tests/AuditRetentionPolicyApiTests.cs` 9/9 PASS | ✅ MET (BE) — UI in Phase 05 |
| §3.2.b | Cấu hình thời gian lưu trữ | `AuditRetentionPolicy` aggregate (Slice B) + CRUD (Slice D2) + purge job consumes precedence — `AuditLogPurgeJobResolveTests` 6/6 PASS | ✅ MET |
| §3.2 (immutability) | Append-only DB enforcement | `tr_AuditLog_NoMutate` trigger (M2 migration) + service-layer guard — `AuditLogImmutabilityTests` 4/4 PASS (raw UPDATE/DELETE rejected; archive bypass succeeds; signature update bypass works) | ✅ MET |

## Test Run Output

```
Passed!  - Failed: 0, Passed: 22, Skipped: 0, Total: 22  AqtCoreFW.Audit.Integration.Tests.dll  (incl. +2 cross-tenant H1+H2 regression guards)
Passed!  - Failed: 0, Passed: 15, Skipped: 0, Total: 15  AqtCoreFW.Audit.Infrastructure.Tests.dll  (incl. AuditLogPurgeJobResolveTests 6/6)
Passed!  - Failed: 0, Passed: 187, Skipped: 0, Total: 187 AqtCoreFW.Audit.Domain.Tests.dll
Passed!  - Failed: 0, Passed: 38, Skipped: 0, Total: 38  AqtCoreFW.Audit.Application.Tests.dll
Passed!  - Failed: 0, Passed: 19, Skipped: 0, Total: 19  AqtCoreFW.Architecture.Tests.dll  (CI guard AuditCategoryAttributeCoverageTests)
Passed!  - Failed: 0, Passed: 125, Skipped: 0, Total: 125 AqtCoreFW.SharedKernel.Tests.dll
Passed!  - Failed: 0, Passed: 26, Skipped: 0, Total: 26  AqtCoreFW.Identity.Integration.Tests.dll  (regression check after fixture change)
```

**Total Slice-D2-touched test count:** 432 PASS, 0 FAIL.

## Code Review Findings (addressed)

| ID | Severity | Issue | Resolution |
|----|----------|-------|------------|
| H1 | High | Update handler accepted PUT on any tenant's policy id | Added `ITenantContext` injection + Forbidden guard for cross-tenant + global policy mutation |
| H2 | High | Same hole on Delete + List leak | Same Forbidden guard in Delete handler |
| H3 | High | SESSION_CONTEXT bypass not cleared on exception path → pool poisoning risk | Moved `sp_set_session_context @value=NULL` into `finally` block (wrapped own try/catch) in both `AuditLogRepository.UpdateSignatureAsync` and `AuditLogPurgeJob.ExecuteAsync` |
| M1 | Medium | RowVersion length not validated → mixed 400/409 semantics | Explicit `caller.Length != 8` check returns 400 BadRequest |
| M3 | Medium | Single bucket failure aborts whole purge run | Per-bucket try/catch — log + continue |
| M2 | Medium | Bucket discovery scans full table (no `(TenantId, Category)` index) | Tracked as follow-up — `M1` migration index on `(TenantId, Category, OccurredAt)` exists; baseline acceptable |
| L1 | Low | LINQ vs SQL precedence resolver could drift | Tracked — share or cross-test in housekeeping pass |
| L3 | Low | Cancellation between bucket + cleanup | Cleanup now in `finally` so cancellation no longer leaks bypass |

## Post-Phase-06 Restoration (2026-05-12)

The Phase-06 migration regeneration (commits `1f344588` "drop AqtCoreFW dev DB + delete 217 migration files" + `fde9ec49` "foundation perf surgery") deleted all hand-tuned legacy migrations, including `20260501020000_AddAuditImmutabilityTrigger_M2`, and the regenerated `InitialCreateFoundationV1` snapshot did **not** carry forward (a) the `tr_AuditLog_NoMutate` raw-SQL trigger nor (b) the global-default `AuditRetentionPolicies` seed (originally `InsertData` in `20260501004056_AddAuditCategoryAndSeverity_M1`). Dev DB regenerated without both; prod had not yet received Phase-06 (still carried the trigger from `a280d526`).

Both artifacts were restored by a follow-on migration **`20260512034220_RestoreAuditImmutabilityTriggerAndDefaultRetention`** (QD742-AUDIT-403, 2026-05-12): trigger re-created verbatim via `CREATE OR ALTER` (idempotent); global-default retention policy re-seeded idempotently with explicit `Id = 1` (entity PK is now `bigint` IDENTITY post Phase-04b, so the original fixed-Guid seed could not be reused verbatim — IDENTITY_INSERT used, guarded by `IF NOT EXISTS (... WHERE TenantId IS NULL AND Category IS NULL)`). `AuditLogImmutabilityTests` 4/4 PASS again; `AuditRetentionPolicyApiTests.Delete_GlobalDefault_Returns400` PASS; `Audit.Integration` 30/30. The DDL below remains current — the restoration migration emits the same trigger body.

> **DBA caveat:** the seed uses `SET IDENTITY_INSERT ... ON; INSERT (Id=1, ...)`. If, in the ~11-day window after the Phase-06-regenerated schema went live (≈ `f5124854^`), an admin created a tenant-scoped retention policy via the API, IDENTITY would have started at 1 and that row now occupies `Id=1` → the `IF NOT EXISTS` guard skips the seed (no PK collision, but the global default is then absent). In that case insert the global default manually (`AuditRetentionPolicy.Create(null, null, 365)` via the CRUD API or a one-off script).

## DDL Evidence

**M2 trigger** (originally `20260501020000_AddAuditImmutabilityTrigger_M2.cs`, dropped by Phase-06, restored by `20260512034220_RestoreAuditImmutabilityTriggerAndDefaultRetention.cs`):

```sql
CREATE OR ALTER TRIGGER [audit].[tr_AuditLog_NoMutate]
ON [audit].[AuditLogEntries]
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @mode nvarchar(32) =
        ISNULL(CAST(SESSION_CONTEXT(N'audit_mutate_mode') AS nvarchar(32)), N'');
    IF @mode = N'archive' OR @mode = N'signature' RETURN;
    RAISERROR('AuditLogEntries is append-only (QĐ 742). ...', 16, 1);
    ROLLBACK TRANSACTION;
END;
```

## Files Delivered (Slice D2)

**Created:**
- `src/modules/audit/AqtCoreFW.Audit.Domain/Repositories/IAuditRetentionPolicyRepository.cs`
- `src/modules/audit/AqtCoreFW.Audit.Infrastructure/Persistence/AuditRetentionPolicyRepository.cs`
- `src/modules/audit/AqtCoreFW.Audit.Infrastructure/Jobs/AuditLogPurgeJob.cs`
- `src/modules/audit/AqtCoreFW.Audit.Infrastructure/Persistence/Migrations/20260501020000_AddAuditImmutabilityTrigger_M2.cs` + Designer
- `src/modules/audit/AqtCoreFW.Audit.Application/DTOs/AuditRetentionPolicyDto.cs`
- `src/modules/audit/AqtCoreFW.Audit.Application/Queries/GetAuditRetentionPolicies/{Query,Handler}.cs`
- `src/modules/audit/AqtCoreFW.Audit.Application/Commands/{Create,Update,Delete}AuditRetentionPolicy/{Command,Handler,Validator}.cs` (3×3 = 9 files; Delete has no validator beyond marker)
- `src/modules/audit/AqtCoreFW.Audit.Presentation/Controllers/AuditRetentionPoliciesController.cs`
- `tests/integration/AqtCoreFW.Audit.Integration.Tests/AuditLogImmutabilityTests.cs`
- `tests/integration/AqtCoreFW.Audit.Integration.Tests/AuditRetentionPolicyApiTests.cs`
- `tests/unit/AqtCoreFW.Audit.Infrastructure.Tests/Jobs/AuditLogPurgeJobResolveTests.cs`

**Modified:**
- `src/modules/identity/AqtCoreFW.Identity.Application/Authorization/PermCodes.cs` (+2 perms: AUDIT.RETENTION.{VIEW,MANAGE}; total 124→126)
- `src/host/AqtCoreFW.Api/E2ETestDataSeeder.Permissions.cs` (matching seed)
- `src/host/AqtCoreFW.Api/Program.cs` (call `AddAuditRecurringJobs()`)
- `src/modules/audit/AqtCoreFW.Audit.Infrastructure/InfrastructureRegistration.cs` (DI + recurring job registration)
- `src/modules/audit/AqtCoreFW.Audit.Infrastructure/Persistence/AuditLogRepository.cs` (`UpdateSignatureAsync` opens connection + sets SESSION_CONTEXT bypass)
- `tests/shared/AqtCoreFW.Tests.SharedFixtures/LocalSqlHelper.cs` (set SESSION_CONTEXT bypass before truncate so trigger doesn't zombie the txn)

## Plan Deviations

1. **Migration M1/M2 split:** Plan §"Migration Split" called for M1=NULLABLE+backfill, smoke 24-48h, then M2=NOT NULL flip + trigger. Slice C shipped M1 as NOT NULL DEFAULT (placeholder values dropped post-backfill); M2 here is trigger-only. End state identical, fewer ALTERs. Documented in M2 migration header comment.
2. **Smoke gate timing:** Migrations M1+M2 ship in same release. Plan's smoke window applies to PROD deployment cadence (DBA can pause between M1 and M2 if desired); in dev/staging both apply at boot.
3. **RLS on AuditRetentionPolicies:** NOT added. Tenant scoping enforced at API layer (`tenantContext.IsSystemAdmin` filter in controller + repository List query). Rationale: nullable TenantId for global default precludes a clean RLS predicate. Tracked as known limitation; revisit if Phase 05 UI exposes per-tenant SELECT path.

## Known Pre-existing Issue (NOT introduced by Slice D2)

`tests/integration/AqtCoreFW.Cases.Integration.Tests/CaseLifecycleReturnIntegrationTests.cs:116` — 3 tests fail with 403 Forbidden on `Return_FromUnderReview_*`. File unmodified by this slice; CASES module untouched. Not blocking compliance evidence.

## Outstanding Backlog (deferred from Slice D1)

**AUDIT-IBC-1:** 8 Identity commands implement raw `IRequest<Result>` instead of `ICommand` → bypass AuditBehavior pipeline → no automatic audit. Slice D1 noted; punted to next housekeeping pass. Affected: `DelegateRoleCommand`, `RevokeDelegationCommand`, `WithdrawConsentCommand`, `GrantConsentCommand`, `ApproveDelegationCommand`, `CreateTenantCommand`, `UpdateTenantCommand`, `ChangeTenantStatusCommand`. Mitigation paths: (a) migrate to `ICommand`, (b) inline `IAuditService.LogAsync` call in handlers.
