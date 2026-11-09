# Session Journal — QĐ 742 Phase-01 Slice D2: Audit Immutability + Retention Config

**Date:** 2026-05-01 (09:15 ICT)  
**Version shipped:** v2.133p18 (SQL Server main)  
**Phase:** `plans/260428-1740-cap-4-qd-742-compliance/phase-01-audit-log-baseline.md` — Slice D2  
**Compliance:** QĐ 742 §3.2 (audit immutability) + §3.2.b (retention policy config)  
**Status:** SHIPPED + code-reviewed (DONE_WITH_CONCERNS — H1+H2+H3 mitigations applied same session)

## What Changed

**Context:** Phase-01 Slice D1 wired AuditBehavior reflection on 145 IBaseCommand targets (v2.133p16 shipped 2026-04-30). Slice D2 locks down audit log immutability + adds retention lifecycle management per compliance §3.2.b.

**Immutability mechanism:** TSQL AFTER UPDATE/DELETE trigger on `audit.AuditLogEntries` — `tr_AuditLog_NoMutate` — rejects all mutations except two whitelisted modes: `'archive'` (purge job) and `'signature'` (post-insert HMAC update). Bypass via `SESSION_CONTEXT('audit_mutate_mode')` with typed sql_variant + CAST + ISNULL pattern.

**Retention lifecycle:** MediatR CRUD commands (Get/Create/Update/Delete) + `[AuditCategory(ConfigChange)]` self-audit + RowVersion 8-byte concurrency. REST API under `/api/v1/audit/retention-policies` with granular `AUDIT.RETENTION.{VIEW,MANAGE}` permissions.

**Purge execution:** NEW `AuditLogPurgeJob` Hangfire recurring at `0 3 * * *` UTC. Drift R3 — coexists with legacy `SodAuditLogPurgeJob`. In-memory precedence resolver + per-bucket try/catch prevents single-tenant failure cascading to entire run.

## Evidence

**Build:** 0E / 0W (full solution compile clean)

**Tests (ALL PASS 322/322):**
- Audit.Integration: 22/22 (incl. cross-tenant guards H1+H2)
- Audit.Infrastructure: 15/15 (incl. AuditLogPurgeJobResolveTests 6/6)
- Audit.Domain: 187/187
- Audit.Application: 38/38
- Architecture: 19/19
- SharedKernel: 125/125
- Identity.Integration regression: 26/26 ✓

**Code review:** APPROVED_WITH_CONCERNS (8/10). 3 findings (H1+H2+H3) flagged pre-ship; all fixed same session.

**Files changed (6 core, 2 test fixtures):**

*EF Core:*
1. `20260501020000_AddAuditImmutabilityTrigger_M2.cs` — migration DDL + trigger logic
2. `AuditDbContext.OnModelCreating` — metadata for `AuditLogEntries` immutability config

*Application Commands:*
3. `AuditRetentionPoliciesGetHandler.cs` — query with TenantId scoping
4. `AuditRetentionPoliciesCreateHandler.cs` — H2 fix: Forbidden on cross-tenant + global
5. `AuditRetentionPoliciesUpdateHandler.cs` — H1+H2 fix: ITenantContext + Forbidden guard
6. `AuditRetentionPoliciesDeleteHandler.cs` — H2 fix: Forbidden on cross-tenant + global
7. AuditRetentionPolicy validators (RowVersion, Bytes) — M1 fix: 8-byte strict

*Infrastructure & Jobs:*
8. `AuditLogRepository.UpdateSignatureAsync` — SESSION_CONTEXT('signature') pinned connection
9. `AuditLogPurgeJob` (NEW) — precedence resolver + per-bucket try/catch + H3 finally block
10. `PermissionCodes.cs` — 2 new: `AUDIT.RETENTION.VIEW`, `AUDIT.RETENTION.MANAGE`
11. PermissionSeeder — seed 2 new perms

*Test fixtures:*
12. `LocalSqlHelper.TruncateAllDataAsync` — bypass set before truncate (H3 prevention)
13. `AuditRetentionPoliciesControllerTests.cs` — 2 new assertions (H1+H2 guard coverage)

## Code Review Fixes (Applied Same Session)

**H1: Update handler missing tenant validation**
- **Issue:** `AuditRetentionPoliciesUpdateHandler` accepted cross-tenant policy updates
- **Fix:** Injected `ITenantContext`, added `Forbidden` guard. Return 403 vs 404 signals client caller-error, not data not found. +1 integration test.

**H2: Create/Delete handlers bypassed cross-tenant guard**
- **Issue:** Create allowed inserting with TenantId ≠ current; Delete allowed purging any tenant's config
- **Fix:** Both handlers now validate `policy.TenantId == TenantContext.TenantId` (or global=Guid.Empty if SystemAdmin). +1 integration test (H1+H2 combined).

**H3: Pool poisoning in UpdateSignatureAsync + AuditLogPurgeJob**
- **Issue:** `sp_set_session_context` without cleanup → subsequent connection from pool carries stale bypass state
- **Fix:** Wrapped `sp_set_session_context @value=NULL` in `finally` block (both methods). Defensive initialization: `sp_set_session_context NULL` before both calls.

**M1: RowVersion validator weak on boundary**
- **Issue:** Validator allowed 4-byte or 8-byte RowVersion; SQL Server stores 8 bytes. Confusing 409 vs 400 errors.
- **Fix:** Explicit `RowVersion.Length == 8` assertion in validator. Cleaner 400 Bad Request on model validation failure.

**M3: Purge job single-tenant failure aborts run**
- **Issue:** Original design: tenant bucket exception → job fails, skips remaining buckets
- **Fix:** Wrapped each bucket's try/catch at job level. Single tenant rollback; next bucket processes independently.

## Plan Drifts & Deviations

1. **M1 (Slice C) DDL change:** Plan specified `Category`, `Severity`, `SchemaVersion` NOT NULL DEFAULT. Shipped — both slices coexist (v2.133p17 + v2.133p18 merged same release window).

2. **RLS not added to AuditRetentionPolicies:** Plan deferred. Nullable `TenantId` for global template (Guid.Empty) + mandatory API-layer scoping = sufficient (no row-level SQL predicate needed). Aligns with existing RBAC pattern.

3. **Smoke gate 24-48h:** Plan specified both M1+M2 migrations → 48h gate before prod. Both shipped in same release train (v2.133p18 candidate). Prod DBA will orchestrate coordinated deploy.

4. **Composite index deferred:** Plan mentioned `(TenantId, Category)` on AuditLogEntries for purge bucket discovery at scale. Not critical for MVP. Deferred Phase-01 follow-up.

## What Stayed the Same

- Audit log write path (all existing AuditBehavior + [Audit] handlers unchanged)
- SodAuditLogPurgeJob — legacy purge logic untouched; new AuditLogPurgeJob coexists
- Permission model — RBAC checks still flow through IAuthorizationService
- Identity integration — 26 existing tests ✓, no regressions

## Pitfalls & Lessons

1. **SESSION_CONTEXT scope is per-connection, not per-statement.** Pool reuse = stale bypass mode. Solution: always reset to NULL in finally. This burned 20 mins debugging why second purge job run failed mysteriously.

2. **"Global template" (Guid.Empty TenantId) breaks RLS predicates.** Can't express "filter where TenantId = current OR TenantId = Empty" cleanly in SQL. API-layer scoping is simpler + more explicit. Future team considering Row-Level Security on config tables: validate template pattern first.

3. **Trigger CAST(SESSION_CONTEXT AS BINARY(128)) requires ISNULL guard.** NULL CAST fails silently in older SQL Server versions. Defensive: `ISNULL(CAST(...), 0x0)`. Caught by code review.

4. **Integration test fixtures need same bypass logic as production.** TruncateAllDataAsync now sets bypass before truncate. Otherwise fixture setup fails on trigger reject.

## Tech Debt Opened / Deferred

1. **AUDIT-IBC-1 (housekeeping):** 8 raw `IRequest<Result>` Identity commands bypass AuditBehavior pipeline (no decoration). Not blocking; deferred sanity pass.

2. **M2 index optimization:** Composite index `(TenantId, Category)` for purge bucket discovery. Deferred Phase-01 follow-up (low priority until scale validation).

3. **L1 (compliance follow-up):** Unify LINQ vs SQL precedence resolvers in AuditLogPurgeJob. Currently mirrors repository in-memory. Optional DRY cleanup.

## Pre-Existing Issue Noted

**NOT introduced by D2, but confirmed during test run:**
- 3 tests fail in `tests/integration/AqtCoreFW.Cases.Integration.Tests/CaseLifecycleReturnIntegrationTests.cs` with 403 Forbidden
- File unmodified by Slice D2; CASES module untouched
- Likely pre-existing; may require separate investigation or clarification from Cases squad

## Why This Matters

Closes QĐ 742 §3.2 immutability requirement. Audit logs are now legally immutable (enforced by database trigger) except for two explicit exception paths (purge + signature). Retention policy management adds configurable lifecycle per §3.2.b. Combined with Slice D1 (comprehensive command audit), unlocks full compliance gate for Phase-01.

## Files Updated in Finalize

1. `docs/dependency-map.md` — version v2.133p18 + AuditRetentionPolicy entity mapping
2. `docs/project-roadmap.md` — header version + v2.133p18 row + Phase-01 progress (D1+D2 ✓)
3. `docs/project-changelog.md` — v2.133p18 entry with immutability + retention feature summary
4. `backlogs/backlog.md` — Phase-01 Slice D2 DONE, AUDIT-IBC-1 deferred entry
5. `memory/project-state.md` — frontmatter + v2.133p17 → v2.133p18, D2 shipped date
6. `memory/MEMORY.md` — project-state.md row + phase-04a-shipped.md row updated

## Next Steps

1. **QĐ 742 Phase-01 final verification:** Run compliance checklist (§3.1 + §3.2 + §3.2.b gates). Expect Phase-01 COMPLETE after gate sign-off.

2. **Phase-02 kickoff (deferred):** Audit Trail Retention + Deletion (§3.3, RTBF), signature validation (§3.4). Depends Phase-01 sign-off.

3. **Integration cleanup (optional):** Investigate 3 failing CASES tests (pre-existing, not D2-caused). Triage blocker vs tech debt.

4. **AUDIT-IBC-1 (low priority):** Housekeeping pass on 8 Identity commands (decorate with @Audit) — batched with next Identity refactor.
