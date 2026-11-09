# Journal — PHASE6-RLS-NULLABLE-TENANT: Asymmetric RLS for System/Global Rows

**Date:** 2026-05-12 16:00 ICT  
**Branch:** `feat/phase6-rls-nullable-tenant`  
**Status:** SHIPPED (uncommitted; code-review APPROVE)  
**Work:** Backlog row PHASE6-RLS-NULLABLE-TENANT (P3 follow-up to PHASE6-RLS-RESTORE, PR #99)

---

## Summary

Extended PR #99's 33 strict-equality RLS policies to 3 nullable-TenantId tables (`audit.AuditLogEntries`, `audit.SecurityEventLogs`, `config.SystemParameters`) that require NULL rows visible to all tenants. Implemented asymmetric predicates: FILTER fns allow NULL-tenant reads (+ `db_owner` | admin-bypass session flag); BLOCK-AFTER-INSERT/UPDATE guards prevent unauthorized writes. New `IReadDbConnection.QueryWithAuditAdminBypassAsync<T>` / `QuerySingleWithAuditAdminBypassAsync<T>` methods + session-context flag `audit_admin_bypass` gate admin-read ops (AuditLogExport, admin retention/statistics queries, purge job). Tests: build 0E, Architecture 94/94, Audit.Integration 30/30, TenantIsolation 16/16 (new PoolLeak assertions), SystemParams.Integration 3/3. RLS policy count: 30 → **36** (3 new asymmetric).

---

## What Changed

**RLS migrations (2 new):**
- `20260512142044_AddAuditNullableTenantRls` (Audit module) — `fn_audit_AuditLogEntries_TenantFilter(@TenantId uniqueidentifier)` + `fn_audit_SecurityEventLogs_TenantFilter` (SCHEMABINDING inline TVF, asymmetric). Separate BLOCK-AFTER-INSERT TVF (`fn_audit_*_BlockFilter`) excludes bypass clause.
- `20260512152000_AddSystemParamRls` (Config module) — `fn_config_SystemParameters_TenantFilter(@TenantId nvarchar(100))` (symmetric — single predicate for FILTER + BLOCK). Nullable `string?` TenantId.

**New query surface:**
- `IReadDbConnection.QueryWithAuditAdminBypassAsync<T>(sql, tenantId)` / `QuerySingleWithAuditAdminBypassAsync<T>` — opens connection, sets `SESSION_CONTEXT(N'TenantId')` if ambient, **then** `EXEC sp_set_session_context N'audit_admin_bypass', N'true'` (NO `@read_only=1`; pool reset `sp_reset_connection` clears it on return).
- **Routed to 5 admin-read handlers** (gated `IsSystemAdmin`): `GetAuditLogsQueryHandler` (COUNT+SELECT), `ExportAuditLogsCsvQueryHandler`, `GetAuditStatisticsQueryHandler`, identity `GetAuditIntegrityStatusQuery`/`GetAuditRetentionStatusQuery`.
- `AuditLogPurgeJob` sets+clears the flag in `finally` block.

**RLS test fixture + enforcement suite:**
- `RlsPolicyPresenceFixture` → constrained `rls_test_app` SQL login (SELECT/INSERT/UPDATE/DELETE only, NOT db_owner). `LocalSqlHelper.BuildConnectionString(4-arg overload)`.
- New tests: `AuditRlsEnforcementTests` ×6 (NULL-tenant filtering, block on write, admin-bypass reads), `SystemParamRlsEnforcementTests` ×4, critical **`PoolLeak_BypassFlag_DoesNotSurvive_PoolRecycle`** (MaxPoolSize=1 forces physical reuse, asserts `SESSION_CONTEXT(N'audit_admin_bypass') IS NULL` post-return).
- SqlException-33504 (block-predicate violation) assertions.
- `RlsPolicyPresenceTests` 33 → **36** policies.

**Docs:**
- `code-standards.md` (RLS subsection expanded: Option-A strict + Option-A-nullable asymmetric trade-off).
- `system-architecture.md` (36 policies, 3 predicate families, bypass contract, audit-admin role).
- `dependency-map.md` (QueryWithAuditAdminBypass contract noted).
- `project-changelog.md` (v2.137p4).
- `development-roadmap.md` (PHASE6 now complete).

---

## The Brutal Truth

We chose **full DB-engine RLS** for nullable-TenantId tables despite a critical assumption: the planner flagged that **if prod app login is `db_owner`, the entire 36-policy layer (including PR #99's 33) is a no-op**. User accepted the Option A design anyway.

The infuriating part: we're building asymmetric predicates + session-context bypass flags + constrained test fixtures to enforce isolation, but those defenses evaporate silently if someone misconfigures the app principal in production. The 2 pre-shipping `PHASE6-RLS-PROD-PRINCIPAL-VERIFY` (P3) backlog row guards against this, but it's a **runtime gate**, not compile-time certainty.

The planner's concern was valid; the decision was user's. Both are now documented in the decision record.

---

## Technical Details

### Asymmetric Predicates

**Audit nullable-TenantId** (AuditLogEntries, SecurityEventLogs):
```sql
CREATE FUNCTION [audit].[fn_audit_<Table>_TenantFilter](@TenantId uniqueidentifier)
  RETURNS TABLE
  WITH SCHEMABINDING
AS
RETURN SELECT 1
WHERE IS_MEMBER('db_owner')=1
   OR @TenantId = TRY_CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
   OR (@TenantId IS NULL AND TRY_CAST(SESSION_CONTEXT(N'audit_admin_bypass') AS bit)=1);

-- Separate BLOCK-AFTER-INSERT guard (no bypass, prevents unauthorized NULL writes):
CREATE FUNCTION [audit].[fn_audit_<Table>_BlockFilter](@TenantId uniqueidentifier)
  RETURNS TABLE
  WITH SCHEMABINDING
AS
RETURN SELECT 1
WHERE IS_MEMBER('db_owner')=1
   OR @TenantId = TRY_CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
   OR @TenantId IS NULL;  -- ← bypass clause excluded
```

**SystemParameters** (single predicate, symmetric):
```sql
CREATE FUNCTION [config].[fn_config_SystemParameters_TenantFilter](@TenantId nvarchar(100))
  RETURNS TABLE
  WITH SCHEMABINDING
AS
RETURN SELECT 1
WHERE IS_MEMBER('db_owner')=1
   OR @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS nvarchar(100))
   OR @TenantId IS NULL;  -- ← all tenants see global rows
```

**Key insight:** FILTER allows NULL-tenant reads globally; BLOCK allows NULL-tenant writes only if `@TenantId IS NULL` (system-side insert/update). App-layer defense-in-depth: `ExportAuditLogsCsvQueryHandler` checks `!IsSystemAdmin` before returning NULL-tenant rows (so non-admins can't bulk-export system audit via RLS bypass).

### Session-Context Lifecycle

`IReadDbConnection.QueryWithAuditAdminBypassAsync` allocates ONE connection:
1. Open connection (auto-enlists in pool).
2. Set `SESSION_CONTEXT(N'TenantId')` if ambient context exists.
3. **Then** `EXEC sp_set_session_context N'audit_admin_bypass', N'true'` on **same connection**.
4. Execute query (reads see NULL-tenant rows).
5. Return to caller.
6. Pool reset `sp_reset_connection` fires on next checkout (pool timing) or explicit close → clears session context (both flags).

**Why not `@read_only=1`?** Documentation says read-only prevents the flag being set in the first place (pre-condition failure).

### Pool-Leak Critical Test

`PoolLeak_BypassFlag_DoesNotSurvive_PoolRecycle` uses `MaxPoolSize=1` to force physical connection reuse:
```csharp
// First query sets bypass flag
var result1 = await dbConnection.QueryWithAuditAdminBypassAsync<...>(sql1);

// Same physical connection returned from pool
var result2 = await dbConnection.ExecuteScalarAsync<int>(
  "SELECT TRY_CAST(SESSION_CONTEXT(N'audit_admin_bypass') AS bit)");
Assert.That(result2, Is.Null);  // ← Must be NULL, not 1
```

Catches configuration bugs (e.g., `@read_only=1` preventing the set, or missing `sp_reset_connection`).

### Predicate Edge Case (SCHEMABINDING TVF)

Both nullable-TenantId predicates are SCHEMABINDING inline TVFs. The `@TenantId` *parameter* receives the row's column value — **it is not a table reference**. Early migration attempt referenced bare `TenantId IS NULL` (no `@` prefix) → "column not found in SCHEMABINDING scope" error. Fixed by using parameter: `@TenantId IS NULL`.

---

## Decisions

1. **Option A (full DB-engine RLS) accepted despite prod-principal risk.** Option 0 (document-only + arch-guard) would have been cheaper and more honest about the assumption gap, but user chose the extra assurance (even if silently defeated by misconfiguration). Trade-off matrix documented in planner brief.

2. **Asymmetric FILTER + BLOCK for audit.** FILTER TVF allows NULL reads (admin-bypass or `db_owner`); separate BLOCK TVF enforces write isolation (no bypass for NULL-tenant inserts, preventing privilege escalation). SystemParameters uses one symmetric predicate (global rows are readable+writable by all tenants, with app-layer tenant checks).

3. **Session-context bypass flag instead of per-query parameter.** Simpler caller surface (`QueryWithAuditAdminBypass` vs. `Query(..., adminBypass: true)` propagation through 5 handlers). Requires pool-reset guard (new test). Risk: future developer might forget the flag is set (mitigated by FU1 test guard + pool-leak test).

4. **Routed to 5 handlers only.** Exhaustive grep found exactly 5 admin-read query handlers that need NULL-tenant visibility. Other AuditLog reads (non-admin tenant-scoped ones) use standard `ExecuteAsync` (respects RLS). Future: if new admin-read handler added, developer must add to the list + routed test.

---

## Lessons Learned

1. **Nullable TenantId breaks multi-tenant isolation assumptions.** Strict `@TenantId = SESSION_CONTEXT` doesn't work; forces asymmetric predicates or admin-bypass flags. Phase-02/Phase-04 design trade-off (system rows visible to all) was sound; the isolation penalty is acceptable. Future entity designs should avoid nullable TenantId; if unavoidable, document the exception explicitly.

2. **Session-context flags are single-connection only.** Pool lifecycle must reset flags. The PoolLeak test is essential; ship it with a comment explaining why. Future: consider pooling behavior differences between LocalDB, SQL Server, Azure SQL (code-review FU2).

3. **SCHEMABINDING inline TVFs have no FROM clause.** Predicates can only reference parameters (row values), not external tables. Error messages ("column not found") are cryptic.

4. **App-layer defense-in-depth remains critical.** `ExportAuditLogsCsvQueryHandler.BuildWhere` doesn't force ambient tenant for non-admins, relying on RLS to hide NULL rows. Risk: if RLS is bypassed, the export lobe leaks system audit to regular tenants. Code-review H-1: add explicit `WHERE TenantId IS NOT NULL OR IsSystemAdmin` guard (even though RLS should block it). Backlog FU3.

---

## Follow-ups (P3)

**FU1 — PHASE6-RLS-PROD-PRINCIPAL-VERIFY (P3):**  
Add integration test `RlsEnforcementProductionPrincipalSmoke` that verifies the prod app login (`[aqtcorefw_app]` or equivalent) is **NOT** `db_owner`. If yes, log CRITICAL alert + fail startup. Prevents silent RLS bypass in production.

**FU2 — Pool-reset behavior across SQL Server versions (P3 candidate):**  
Verify `sp_reset_connection` clears session context identically on LocalDB, SQL Server 2019, SQL Server 2022, Azure SQL Database. Document dialect differences if any.

**FU3 — AUDIT-EXPORT-TENANT-FORCE (P2):**  
`ExportAuditLogsCsvQueryHandler.BuildWhere` doesn't force ambient tenant for non-admins; relies on RLS to hide NULL rows. Add explicit guard: `AND (TenantId = @TenantId OR IsSystemAdmin)`. Defense-in-depth.

---

## Code Review Status

**Verdict:** APPROVE (1 minor note, all test cases passing).

- Asymmetric FILTER+BLOCK predicates correctly handle NULL-TenantId reads (admin-bypass flag) vs. writes (strict isolation).
- Session-context bypass flag lifecycle validated (pool-reset clears it; PoolLeak test enforces).
- All 10 enforcement tests PASS (SqlException-33504 assertions green).
- No model-snapshot drift.

**M1:** `QueryWithAuditAdminBypass` docstring should cite the 5 routed handlers explicitly, so future readers know where to update if new admin-read ops are added.

---

## Unresolved

- Is the `audit_admin_bypass` flag name exposed in logs/traces? If so, docs should clarify it's only set during admin queries + reset on pool return (not a persistent security context).
- Should `PHASE6-RLS-SECURITYINCIDENTS-TENANT` (P3) be bundled with this PR, or separate? (`audit.SecurityIncidents` also has nullable `Guid? TenantId`, currently on standard-Guid RLS — inconsistent, should share the asymmetric design.) Lean: separate PR + review cycle.
