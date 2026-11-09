# Session Journal — T5-16 Phase 3: Trimmed Scope — Actual Test Run

**Date:** 2026-04-15
**Task:** T5-16 Phase 3 (trimmed) — run integration tests, fix bugs, docs sync
**Prior phases:** Phase 1 (BE, commit 95155652) + Phase 2 (FE, commit 3cc12a11)
**Prior Phase 3 session:** `journal-260415-1347-t5-16-phase-03-verification.md` — that session
  WROTE the test files but never ran them to completion. Docs were prematurely updated as COMPLETE.

---

## Summary

This session ran the integration tests for the first time and fixed 3 pre-existing bugs discovered
during the run. Final result: 15/15 tests pass, 2 perf gates skipped as expected, 0 regressions.

---

## Scope Trim (user decision, carried from prior session)

**DROPPED:**
- Playwright E2E spec (`workflow-dashboard-cursor-advance.spec.ts`)
- Rate-limit quota exhaustion test
- Perf gates as active tests (kept as `[Fact(Skip)]`)

**KEPT:**
- 8 endpoint integration tests (list + summary: 401/403/200/400)
- 2 keyset correctness tests (120-row walk + tenant isolation)
- Docs sync (dependency-map Section 4, backlog T5-16-FOLLOWUP, project-state)

---

## Bugs Discovered and Fixed

### Bug 1: xUnit v2 `[CollectionDefinition]` assembly scoping

**Root cause:** xUnit v2 discovers `[CollectionDefinition]` only in the executing test assembly.
`SqlServerCollection` (with `[CollectionDefinition("SqlServer")]`) lives in
`AqtCoreFW.Tests.SharedFixtures.dll` — a referenced assembly that xUnit never scans at runtime.
Result: xUnit sees `[Collection("SqlServer")]` on test classes but finds no matching definition;
treats it as ad-hoc ordering; never instantiates `SqlServerFixture`; `WebAppFixture(SqlServerFixture)`
constructor throws "unresolved constructor argument".

**Fix:** Created `SqlServerCollectionLocal.cs` in the Workflow integration test project —
re-declares `[CollectionDefinition(SqlServerCollection.CollectionName)]` + `ICollectionFixture<SqlServerFixture>`.
This makes xUnit instantiate the fixture within the executing assembly.

**Scope note:** The same latent bug exists in all 16 other module integration test projects.
Pre-existing, out of scope. Those projects may have integration tests that were silently skipped
or errored for the same reason.

### Bug 2: Non-idempotent `DropRowVersionFromWorkflowInstance` migration

**Root cause:** Migration `20260411105147_DropRowVersionFromWorkflowInstance` used
`migrationBuilder.DropColumn(name: "RowVersion", table: "WorkflowInstances")` without a guard.
However, migration `20260329141950_AddBranchChildStatusIdToWorkflowTask` (earlier timestamp)
**also** drops `RowVersion` from `WorkflowInstances` at its line 14-17.

On a fresh Testcontainers DB, ALL migrations run in sequence:
1. `20260314_InitialCreate` — creates `WorkflowInstances` WITH `RowVersion`
2. `20260329_AddBranchChildStatusIdToWorkflowTask` — drops `RowVersion` (line 14)
3. `20260411105147_DropRowVersionFromWorkflowInstance` — tries to drop again → FAIL

On production DBs this was invisible because `20260329` ran when `RowVersion` existed,
and `20260411` ran on the same DB where it was already gone — but the migration tool
marks it as "applied" without re-running. On Testcontainers (fresh DB), all migrations
replay from scratch in timestamp order, exposing the idempotency issue.

**Fix:** Replaced `DropColumn()` with raw `IF COL_LENGTH(...) IS NOT NULL` guard SQL:
```sql
IF COL_LENGTH('[workflow].[WorkflowInstances]', 'RowVersion') IS NOT NULL
BEGIN
    -- drop default constraint + column
END
```

### Bug 3: Cross-test row bleed in `WorkflowInstancesKeysetCorrectnessTests`

**Root cause:** Tests used static `TenantA = Guid.Parse("A0000001-...")` and `TenantB` GUIDs.
The Testcontainers DB is shared across all tests in the collection — rows from one test persist
when the next test runs. `TenantIsolation` seeded 60 rows into `TenantA`; `Walk3Pages` then
found 180+ rows in `TenantA` (120 seeded + 60 from TenantIsolation), making page 3 non-final.

Secondary bug: `TenantIsolation` called `SeedWorkflowInstancesAsync(0, TenantB)` at the end
to "retrieve" TenantB IDs, but the helper returns `[]` when `count == 0`. Then
`.NotContain([])` (empty collection) threw `System.ArgumentException: Cannot verify
non-containment against an empty collection`.

**Fix:** Removed static `TenantA`/`TenantB` from class fields. Each test generates
`Guid.NewGuid()` for its tenant(s), ensuring complete isolation within the shared DB.
`seededB` IDs are captured directly from the seed call, not re-fetched.

---

## RT-07 Assertion Gap (addressed)

The pre-existing `ListInstances_AuthenticatedWithPermission_Returns200PaginatedEnvelope`
test didn't assert `totalCount: null` or `pageSize` in meta. Added:
- `data.totalCount` if present must be JSON null (not an integer) [RT-07]
- `meta.pageSize` must be present and > 0
- `meta.hasNextPage` must be a boolean

Note: `totalCount` is NOT present in the JSON response (null values appear to be omitted by the
serializer in this codebase — no global `WhenWritingNull` policy found, but the property is absent
in practice). The assertion accepts both "present as null" and "absent" as compliant with RT-07.

---

## Test Results

| Test | Result |
|------|--------|
| `GetSummary_Unauthenticated_Returns401` | PASS |
| `GetSummary_AuthenticatedWithoutPermission_Returns403` | PASS |
| `GetSummary_AuthenticatedWithPermission_Returns200WithKpiFields` | PASS |
| `ListInstances_Unauthenticated_Returns401` | PASS |
| `ListInstances_AuthenticatedWithoutPermission_Returns403` | PASS |
| `ListInstances_AuthenticatedWithPermission_Returns200PaginatedEnvelope` | PASS |
| `ListInstances_MalformedCursor_Returns400WithErrorCode` | PASS |
| `ListInstances_RepeatedRequests_EventuallyReturns429OrPass` | PASS |
| `Walk3Pages_120Rows_NoDuplicatesNoGaps_CorrectOrder` | PASS |
| `TenantIsolation_60RowsEachTenant_QueryAsAReturnsOnly60` | PASS |
| `FirstPageList_10KRows_P95Under200ms` | SKIP (Testcontainers hardware) |
| `SummaryEndpoint_10KRows_P95Under500ms` | SKIP (Testcontainers hardware) |
| Pre-existing `WorkflowEndpointsTests` (5 tests) | PASS |
| **Total** | **15 pass, 2 skip, 0 fail** |

---

## Files Created / Modified

| File | Type | Notes |
|------|------|-------|
| `tests/integration/.../SqlServerCollectionLocal.cs` | New | xUnit assembly scoping fix |
| `src/.../Migrations/20260411105147_DropRowVersionFromWorkflowInstance.cs` | Modified | Idempotent guard |
| `tests/integration/.../WorkflowInstancesListEndpointTests.cs` | Modified | RT-07 assertions |
| `tests/integration/.../WorkflowInstancesKeysetCorrectnessTests.cs` | Modified | Per-test tenants, TenantB ID capture |
| `docs/dependency-map.md` | Modified | Section 4: `workflow-instances` row |
| `backlogs/backlog.md` | Modified | Phase 3 corrected + T5-16-FOLLOWUP added |
| `memory/project-state.md` | Modified | Session entry |
| `docs/journals/journal-260415-1531-...md` | New | This file |

---

## Verification Gates

| Gate | Result |
|------|--------|
| 1. `dotnet build` 0 warnings | PASS |
| 2. 6 target integration tests (4 list endpoint + 2 keyset) PASS | PASS |
| 3. No regressions in pre-existing endpoint tests | PASS (5/5) |
| 4. `dependency-map.md` Section 4 `workflow-instances` row added | PASS |
| 5. `backlog.md` T5-16-FOLLOWUP note present | PASS |
| 6. `project-state.md` updated with actual session | PASS |
| 7. Session journal written | PASS — this file |

---

## Deferred Items (T5-16-FOLLOWUP)

- Playwright E2E `@smoke` spec — needs BE port 5002 + dev server in CI
- Rate-limit quota exhaustion test — needs low-limit fixture config
- Perf gates at 10K rows — run against dedicated SQL Server (not Testcontainers)
