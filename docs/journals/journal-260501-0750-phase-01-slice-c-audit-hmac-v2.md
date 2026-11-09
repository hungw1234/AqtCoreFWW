# Journal — Phase-01 Slice C: M1 Migration + HMAC v2 Dual-Schema

**Date:** 2026-05-01 (~07:50 ICT)
**Branch:** main (uncommitted — awaits user approval)
**Plan:** `plans/260428-1740-cap-4-qd-742-compliance/phase-01-audit-log-baseline.md`
**Slice:** C (Steps 6 + 9 per slice plan)

## What Shipped

**M1 EF migration** `20260501004056_AddAuditCategoryAndSeverity_M1` — adds Category, Severity, SchemaVersion columns + AuditRetentionPolicies table + seeds global default retention policy. **AuditService HMAC v2** — dual-schema fingerprint branches on `SchemaVersion` so legacy v1 rows verify intact while new v2 rows tamper-protect Category/Severity.

## Why The Approach Is Different From The Plan

Plan §"HMAC Payload v2" line 161 said v2 fingerprint includes `DataSnapshot`. We deviate: **v2 EXCLUDES DataSnapshot**. Reason: `AuditPiiAnonymizer` mutates DataSnapshot post-write for RTBF / Law 91/2025 erasure. If the chain depends on DataSnapshot, anonymizing PII would invalidate the entire chain after the anonymizer runs — making chain-verify endpoint fail across every anonymized tenant. The existing invariant (codified in `AuditPiiAnonymizer.cs` xmldoc + `AuditPiiAnonymizerTests` docstring) wins over the plan line. Compliance trade-off: snapshot tamper-coverage < RTBF compatibility.

Plan §"Migration Split" said Category/Severity NULLABLE in M1 + `ALTER ... NOT NULL` in M2. We deviate: NOT NULL with placeholder DEFAULT 1, drop DEFAULT post-backfill. End-state equivalent, simpler rollback, one fewer ALTER in M2. Documented in migration header.

## Sentinel Backfill Pattern (worth remembering)

`HasDefaultValue(2)` in EF config + scaffolded `AddColumn defaultValue: 2` would silently overwrite SchemaVersion on every existing row to 2 — their existing HMAC v1 signatures would then route through the v2 fingerprint builder and fail validation across the entire historical chain.

Fix: AddColumn with sentinel `DEFAULT 0` (= "not yet backfilled"). Backfill loop uses `WHERE SchemaVersion = 0` for batch selection AND `SET ..., SchemaVersion = 1` to consume the sentinel. WHILE terminates deterministically even when an action maps back to `(Category=1, Severity=1)` — the SchemaVersion bump is the mandatory consumption marker. Post-loop, ALTER DEFAULT to 2 so future raw INSERTs land on v2.

Pattern is worth copying into other migrations that need destructive-replace semantics on existing rows without a natural UNIQUE column to filter on.

## Drift Guard

`AuditCategoryMapper` (C# domain logic) and the SQL CASE expression in M1 must agree for every `AuditAction` constant. Drift = silent backfill mis-classification across the entire historical audit log.

`AuditCategoryBackfillSqlMirrorTests` pins both directions:
1. C# mirror dict matches `AuditCategoryMapper.Map(action)` for every `AuditAction` constant via reflection.
2. Every key in the C# mirror dict appears as a `WHEN '<KEY>'` clause in the migration SQL file (regex check on disk).

Fails fast in CI if either side is changed without the other.

## Code Review Findings (all addressed in-PR)

- **C-1 (functional):** Migration seeded `ClassificationLevel = 2` with comment "Internal" — but enum is `Public=0, Internal=1, Confidential=2`. Global retention policy was being seeded as Confidential. Fixed: `2 → 1`.
- **H-2 (plan deviation):** Single bulk UPDATE replaced by 50k batched WHILE loop per plan §"Migration backfill chậm". Sentinel-driven termination (SchemaVersion=0 consumed each batch).
- **H-1 (plan deviation, docs-only):** NOT NULL + placeholder DEFAULT vs plan's NULLABLE + ALTER — documented in migration header strategy comment.
- **M-1, M-3, M-4, M-5:** observed but accepted within Slice C scope (smoke gate / chain-splice integration test / DataSnapshot ADR all belong to Slice D smoke gate).

## Test Results

| Suite | Result |
|-------|--------|
| Audit.Infrastructure (incl. 7 new) | 9/9 PASS |
| Audit.Domain | 187/187 PASS |
| Audit.Application | 38/38 PASS |
| SharedKernel | 125/125 PASS |
| Host build (`AqtCoreFW.Api`) | clean, 1m20s |

## What's Next (Slice D, NOT in this slice)

- M2 migration: ALTER Category/Severity NOT NULL formally (effectively no-op after Slice C drops DEFAULTs), CREATE TRIGGER `tr_AuditLog_NoMutate` with `SESSION_CONTEXT('audit_archive_mode')` bypass for the new `AuditLogPurgeJob`.
- AuditBehavior reflection wiring: pass `commandType` to `AuditCategoryMapper.Map` so `[AuditCategory]` attribute precedence works (Slice C calls with `commandType: null` → verb fallback only).
- Decorate 135 IBaseCommand classes with explicit `[AuditCategory(...)]`.
- MediatR handlers + validators + AuditPoliciesController + new `Audit.Policy.View` / `Audit.Policy.Manage` permissions.
- NEW `AuditLogPurgeJob` (Audit module) — drift R3, separate from `SodAuditLogPurgeJob` which purges `SodViolationLogs` not `AuditLogEntries`.
- CI guard `AuditBehaviorAttributeCoverageTests`.

Resume: `/ck:cook plans/260428-1740-cap-4-qd-742-compliance/phase-01-audit-log-baseline.md --tdd` and target Slice D.

## Files Touched

**Migrations**
- NEW `src/modules/audit/AqtCoreFW.Audit.Infrastructure/Persistence/Migrations/20260501004056_AddAuditCategoryAndSeverity_M1.cs` (+ Designer.cs + AuditDbContextModelSnapshot delta)

**Source**
- MODIFIED `src/modules/audit/AqtCoreFW.Audit.Infrastructure/Services/AuditService.cs`
- MODIFIED `src/modules/audit/AqtCoreFW.Audit.Infrastructure/Services/HmacChainService.cs`
- MODIFIED `src/modules/audit/AqtCoreFW.Audit.Infrastructure/AqtCoreFW.Audit.Infrastructure.csproj` (InternalsVisibleTo)

**Tests**
- NEW `tests/unit/AqtCoreFW.Audit.Infrastructure.Tests/Services/HmacChainServiceFingerprintTests.cs`
- NEW `tests/unit/AqtCoreFW.Audit.Infrastructure.Tests/Persistence/AuditCategoryBackfillSqlMirrorTests.cs`

**Plan + Memory**
- MODIFIED `plans/260428-1740-cap-4-qd-742-compliance/phase-01-audit-log-baseline.md` (Slice C status, deliverables section, todo checks)
- MODIFIED `memory/project-state.md` (header + Last Updated)

## Code-Review Report

`plans/reports/code-reviewer-260501-0735-slice-c-audit-hmac-v2.md`
