# Journal — AUDIT-EXPORT-TENANT-FORCE: Defense-in-Depth CSV Export Isolation

**Date:** 2026-05-12 19:40 ICT  
**Branch:** `fix/audit-export-tenant-force`  
**Status:** SHIPPED (code-review APPROVE_WITH_NITS; PR #101 open, not merged)  
**Work:** P2 follow-up from PHASE6-RLS-NULLABLE-TENANT (code-review H-1)

---

## Summary

Fixed **unforced ambient-tenant check** in `ExportAuditLogsCsvQueryHandler.BuildWhere` — a non-admin could dispatch the export query with another tenant's id (or null ambient context) and bypass the CSV row filter entirely. Handler now computes `effectiveTenantId` (ambient for non-admins, request for admins) and enforces it unconditionally, mirroring `GetAuditLogsQueryHandler` exactly. New 7-test suite validates the attack scenario (attacker-supplied TenantId overridden), throws-before-IO on missing ambient context, and CSV UTF-8 BOM+header. Build 0E, Audit.Application.Tests 45/45, Architecture 94/94.

---

## What Changed

**Handler defense-in-depth:**
- `ExportAuditLogsCsvQueryHandler.Handle` now computes:
  ```csharp
  var effectiveTenantId = tenantContext.IsSystemAdmin 
    ? request.TenantId 
    : (tenantContext.TenantId ?? throw new UnauthorizedAccessException(
        "Missing tenant context for audit log export."));
  ```
  Then calls `BuildWhere(effectiveTenantId)` instead of `BuildWhere(request.TenantId)`.
- Throws **before any DB call** on missing ambient (fail-closed). `UnauthorizedAccessException` → HTTP 401 via `GlobalExceptionHandlerMiddleware`.
- **Matches `GetAuditLogsQueryHandler` pattern exactly** (discovered via grep during code-review phase; export handler had silently drifted).

**Mockability fix:**
- `ExportRow` projection: `private` → `internal`.
- New `InternalsVisibleTo` attribute in `AqtCoreFW.Audit.Application.csproj`:
  ```xml
  <InternalsVisibleTo Include="AqtCoreFW.Audit.Application.Tests" />
  <InternalsVisibleTo Include="DynamicProxyGenAssembly2" />
  ```
  Mirrors Identity/Workflow/Forms pattern. Required so NSubstitute/Castle can auto-stub `Task<IEnumerable<ExportRow>>` return type.

**Test suite (7 tests):**
1. `ThrowsUnauthorizedAccessException_WhenTenantContextMissing_AndNotSystemAdmin` — asserts no DB call via `_db.ReceivedCalls().IsEmpty`.
2. `NonAdminAlwaysInjectsAmbientTenant_EvenWhenQueryHasNoTenantId` — simulates request with `null` TenantId.
3. `NonAdminOverridesRequestTenantId_WithAmbientTenant` — **attack scenario**: asserts bound `@TenantId` DynamicParameter equals ambient, not attacker value.
4. `NonAdminUsesNonBypassConnection` — verifies routed through standard `_db`, not `_adminDb`.
5. `SystemAdminCanExportWithoutAmbientContext` — admin bypasses ambient-tenant logic.
6. `ExportAuditLogsCsvQueryHandler_BuildsCsvWithUtf8Bom` — CSV structure.
7. `ExportAuditLogsCsvQueryHandler_SystemAdminBypassRoute` — admin uses `QueryWithAuditAdminBypassAsync`.

Assertions inspect generated SQL + DynamicParameters via `_db.ReceivedCalls()` (reflection approach preserved — `ExportRow` is internal, can't name it directly in assertions).

**Docs:**
- `project-changelog.md`: new entry `v2.137p5-AUDIT-EXPORT-TENANT-FORCE`.
- `backlogs/backlog.md`: marked DONE, spawned follow-up **AUDIT-CSV-FORMULA-INJECTION (P3, ~30m)** — `EscapeCell` has no leading-`=`/`+`/`-`/`@` guard (low risk: Excel-only, audit data system-generated).

---

## The Brutal Truth

Code-review H-1 from the PHASE6-RLS-NULLABLE-TENANT work caught this. The infuriating part: `GetAuditLogsQueryHandler` enforces ambient-tenant isolation correctly; the export handler has identical design goals but drifted. No one noticed because the RLS restore (#99/#100) is a **backstop**, not the primary control. If prod app login is NOT `db_owner` (the critical assumption from Phase6), RLS blocks the leak. If it IS `db_owner`, we just shipped theatre that looks good in code review.

The pattern: when one read handler forces ambient-tenant, grep **all** siblings over the same table that build WHERE clauses. Export handlers are easy to miss because they're often in a separate concern (Csv, Excel, PDF).

---

## Technical Details

### Attack Scenario

Non-admin attacker crafts request:
```csharp
new ExportAuditLogsCsvQuery { TenantId = otherTenantId }
```
**Before fix:** `BuildWhere(request.TenantId)` → WHERE clause filters on otherTenantId → attacker gets other tenant's audit CSV.

**After fix:** `effectiveTenantId = ambient (attacker's own tenant)` → WHERE clause filters on ambient → request.TenantId ignored for non-admin.

### DynamicParameters Inspection via NSubstitute

Test asserts the SQL parameter value:
```csharp
var calls = _db.ReceivedCalls();
var executeCall = calls.FirstOrDefault(c => c.GetMethodInfo().Name == "ExecuteAsync");
var dynamicParams = (DynamicParameters)executeCall.GetArguments()[1];
var boundTenantId = (Guid)dynamicParams.Get<Guid>("TenantId");

Assert.That(boundTenantId, Is.EqualTo(ambientTenant));  // not attacker-supplied TenantId
```

---

## Decisions

1. **Force ambient tenant for non-admins** (no request-supplied override). Matches `GetAuditLogsQueryHandler`. Limits use case: non-admins can only export their own tenant's audit. Admin dashboard can parameterize tenant picker (already does via SystemAdmin branch).

2. **Throw `UnauthorizedAccessException` before DB call** (fail-closed). If ambient tenant is missing, better to reject early than silently export nothing. Mirrors auth guards in query handlers.

3. **`internal` + `InternalsVisibleTo("DynamicProxyGenAssembly2")`** for `ExportRow`. Established repo pattern (Identity/Workflow/Forms) for NSubstitute mockability. Cleaner than widening to `public`.

---

## Lessons Learned

1. **Audit trail enforcement spreads across multiple handlers.** When a core enforce pattern (force ambient tenant) is established, **search adjacent handlers** for drift. Export handlers are the sneaky cases — separate namespace, separate test file, forgotten in the initial sweep.

2. **RLS is NOT a substitute for handler-level checks.** It's a backstop. The prod-principal assumption (#100) means RLS might be invisible (if `db_owner`). Handler-level defense-in-depth **must** hold independently.

3. **NSubstitute/Castle + internal types = `InternalsVisibleTo("DynamicProxyGenAssembly2")`** . Don't widen to `public`; reach for this pattern. See Identity.Application.csproj as reference.

---

## Code Review Status

**Verdict:** APPROVE_WITH_NITS (nits folded before commit).

- Defense-in-depth logic correctly enforces ambient-tenant precedence over request.
- Attack scenario (attacker-supplied TenantId override) validated in test #3.
- Fail-closed behavior (throw before IO) on missing ambient context.
- All 7 tests PASS; no regressions.

**Nits folded:**
- Added `OverridesRequestTenantId` test case (explicit attack scenario assertion).
- Added no-IO assertion to throw-path test (`_db.ReceivedCalls().IsEmpty`).
- Fixed stale docstrings (3 XML doc lines referenced old pattern).

---

## Unresolved

- Should `ExportAuditLogsCsvQueryHandler` also check `TenantId IS NOT NULL OR IsSystemAdmin` in SQL (in case RLS is somehow bypassed by future misconfiguration)? Code-review suggested it; backlog as P3 AUDIT-CSV-DEFENSE-LAYERS.
