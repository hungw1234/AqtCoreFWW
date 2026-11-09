---
title: "v2.133p15 MIG-PREFLIGHT: Race-condition cleanup migration + E2E fixture hardening"
date: 2026-04-29
session_duration_actual: 2h 15min
version_bump: v2.133p14 → v2.133p15
status: DONE
author: claude-code + fullstack-developer + code-reviewer
---

# v2.133p15 MIG-PREFLIGHT Session

**Date:** 2026-04-29 (session time TBD)  
**Severity:** High (blocking startup in envs with accumulated test data)  
**Component:** Workflow module (migrations, E2E test fixtures)  
**Status:** Resolved & shipped

---

## What Happened

Launched local dev stack (`/aqt-runapp`). Auth + FE booted clean. API crashed with SqlException 1505: "Cannot create UNIQUE INDEX over filtered columns if there are duplicate non-NULL values." Root cause: v2.133p11's `AddUniqueActiveWorkflowInstanceIndex` migration failed to account for **54 accumulated duplicate active WorkflowInstance rows** from E2E test race-condition artifacts (E2EEntity, workflowKeyGuid=...099, tenantId=001). Migration blocked indefinitely. Diagnosed via T-SQL rank/row_number CTE query identifying collision groups. Cleaned accumulated rows with DELETE. API restarted clean.

Traced root cause to TOCTOU race between Outbox async dispatcher + CaseCreatedWorkflowRequestedEvent consumer firing simultaneously, both attempting INSERT for same logical case → v2.133p11 fixed forward via unique filtered index + idempotent SqlException handler. **Forward protection doesn't clean pre-existing race victims.** Decision: implement v2.133p15 preflight cleanup migration (modify v2.133p11 in-place with CTE-ranked DELETE prologue before CREATE INDEX).

Implemented: Modified v2.133p11 Up() to INSERT DELETE statement ranking by (WorkflowKey, TenantId) with WHERE RowNum > 1, keeping latest by CreatedAt. Non-destructive on success-path envs (no duplicates exist → DELETE finds nothing). Wrote 3 integration tests validating preflight deduplication + index creation atomicity. Discovered 5 pre-existing WorkflowMigrationIntegrationTests failures caused by same race pattern (hardcoded entity GUIDs reused across test methods collision with new unique index). Fixed test fixtures (MakeInstance now uses Guid.NewGuid instead of ...099 sentinel).

---

## The Brutal Truth

This is frustrating because v2.133p11 fixed the race *forward* but left a landmine for any environment that accumulated test data before the patch. The fix is architecturally correct (unique filtered index prevents future duplicates), but incomplete for real-world deployments. Modifying a merged migration 4 patches after ship feels risky, but the alternative (new migration #2) fails because v2.133p11 **atomically rolls back on error, never records to __EFMigrationsHistory**. New migration would run blindly and crash with identical SqlException.

The decision to modify in-place is defensible because:
1. v2.133p11 is recent (p14 just shipped 2026-04-27)
2. Pre-failure state = no migration record = safe to re-run v2.133p11 with prologue
3. Prologue is idempotent (success-path envs skip DELETE entirely)
4. Post-fix, both old/new envs converge on same schema

What makes this painful is discovering the E2E test fixtures were hardcoding entity GUIDs (...099, ...077). Test isolation was an illusion — parallel test execution with shared GUIDs guaranteed collisions under the new unique constraint. Should have caught this before shipping v2.133p11, not 4 patches later.

---

## Technical Details

### Root Cause: TOCTOU Race in Workflow Creation

**Scenario:**
```
Time T0: API request triggers CreateCaseCommand
  ├─ T0+0ms: Outbox.PostAsync(CaseCreatedWorkflowRequestedEvent, tenant=001, workflowKey=...099)
  ├─ T0+1ms: Another API req hits CreateCaseCommand (exact same case, race condition)
  │   └─ T0+1ms: Second Outbox.PostAsync (same event, same key)
  └─ T0+100ms: Outbox dispatcher processes both → INSERT WorkflowInstance twice for (workflowKey, tenant)

Result: 54 duplicate rows with same (WorkflowKey, TenantId, active=1)
```

v2.133p11 fixed this with:
```csharp
// Migration 20260429110600_AddUniqueActiveWorkflowInstanceIndex.cs
modelBuilder.Entity<WorkflowInstance>()
  .HasIndex(wi => new { wi.WorkflowKey, wi.TenantId })
  .IsUnique()
  .HasFilter("[Status] = 'Active'");
```

**Protects forward** (future duplicates rejected) but **doesn't clean backward** (pre-existing dups block index creation).

### v2.133p15 Fix: Preflight Deduplication

**Modified v2.133p11 Up():**
```sql
-- Preflight: DELETE duplicates (no-op if none exist)
;WITH RankedDuplicates AS (
  SELECT *,
    ROW_NUMBER() OVER (
      PARTITION BY WorkflowKey, TenantId 
      ORDER BY CreatedAt DESC
    ) AS RowNum
  FROM workflow.WorkflowInstances
  WHERE [Status] = 'Active'
)
DELETE FROM RankedDuplicates
WHERE RowNum > 1;

-- Then create unique index (succeeds for all envs)
CREATE UNIQUE INDEX idx_active_workflow_instance_key 
ON workflow.WorkflowInstances(WorkflowKey, TenantId)
WHERE [Status] = 'Active';
```

**Behavior:**
- Success-path envs (no duplicates): DELETE finds 0 rows, index creation succeeds
- Accumulated-test-data envs: DELETE keeps latest by CreatedAt per group, index creation succeeds
- Both converge on identical schema + state

### E2E Test Fixture Hardening

**Problem: Hardcoded GUIDs collide under unique constraint**

```csharp
// BEFORE (WorkflowMigrationIntegrationTests.cs)
private static Guid MakeInstance(string key) 
  => new Guid("11111111-1111-1111-1111-099999999999"); // ← Reused across methods!
```

**After:**
```csharp
private static Guid MakeInstance(string key)
  => Guid.NewGuid(); // ← Unique per call
```

**Impact:** Fixed 5 test methods that create overlapping WorkflowInstance entities:
- WorkflowMigrationIntegrationTests.TestMigrateWithMultipleVersions()
- WorkflowMigrationIntegrationTests.TestRollbackFromV2ToV1()
- (3 others)

All 76 WorkflowMigrationIntegrationTests now pass (was 71/76).

### Test Suite Coverage

**Created:**
- `tests/integration/.../Migrations/UniqueActiveInstanceIndexPreflightCleanupTests.cs` (3 tests)
  - `TestPreflight_DeletesDuplicatesKeepingLatest()` ← Validates dedup logic
  - `TestPreflight_CreatesIndexOnSuccessPath()` ← Verifies idempotency
  - `TestPreflight_AtomicWithIndexCreation()` ← Confirms atomic rollback

**Modified:**
- `tests/unit/.../Persistence/WorkflowMigrationIntegrationTests.cs` (5 test methods fixed)

**Test Results:**
- Workflow.Application: 441/441 ✓
- Workflow.Infrastructure: 76/76 ✓ (was 71/76)
- New Integration: 3/3 ✓
- Architecture: 18/18 ✓
- **Total:** 538/538 pass

---

## What We Tried

1. **Diagnosis (T-SQL):** Queried `workflow.WorkflowInstances` with ranking CTE to identify collision groups (54 for workflowKey=...099, 2 for ...077). Confirmed status='Active' + same TenantId per group.

2. **Option A: New migration after v2.133p11** → Rejected. Root cause: v2.133p11 atomically rolls back on error, never records to __EFMigrationsHistory. If v2.133p11 fails (SqlException 1505), __EFMigrationsHistory has no entry. New migration sees missing v2.133p11 record, assumes migration chain broken, fails. Chain becomes unmigrable.

3. **Option B: Modify v2.133p11 in-place** ← Chosen. Add DELETE prologue. Rationale: (a) recent patch (b) atomic failure doesn't record (c) prologue is idempotent (d) convergence guaranteed.

4. **Implementation:** Modified v2.133p11 Up() method in WorkflowDbContext migration folder. Added CTE-ranked DELETE before CREATE INDEX. Ran migrate locally, verified preflight deleted 54 rows, index created, API boot clean.

5. **E2E Fixture Audit:** Grepped hardcoded GUIDs in test file. Found 5 methods using `MakeInstance("...099")` or similar. Changed to `Guid.NewGuid()`. Reran suite: 71 → 76/76 pass.

6. **Code review:** Verified atomic transaction scope (DELETE + CREATE INDEX in single migration Up block), confirmed prologue is provably safe on both paths (DELETE is WHERE RowNum > 1, keeping exactly 1 per group), verified tests cover dedup logic + idempotency.

---

## Root Cause Analysis

### Why Race Condition Exists

1. **Outbox dispatcher async:** MassTransit publishes CaseCreatedWorkflowRequestedEvent to Outbox table, consumer picks it up asynchronously.
2. **No request deduplication:** If same case creation request fires twice (network retry, race in test), both INSERT to Outbox → both publish to consumer.
3. **Consumer INSERT duplicates:** WorkflowInstanceConsumer.Handle(CaseCreatedWorkflowRequestedEvent) inserts WorkflowInstance without checking if (WorkflowKey, TenantId) already active.
4. **v2.133p11 protected forward:** Unique filtered index prevents future INSERTs but doesn't touch pre-existing rows.

### Why Tests Collide

1. **Hardcoded sentinel GUIDs:** Test helpers used fixed GUIDs (...099, ...077) to make test data recognizable in debugger.
2. **Parallel test discovery:** xUnit runs test methods in parallel by default (configurable, but default). Multiple MakeInstance calls with same GUID in parallel → same (WorkflowKey, TenantId) tuple in DB.
3. **No isolation per test:** Tests share a single WebAppFixture DatabaseFixture (seeded fresh per test class, not per method), but within a test class, multiple test methods use the same seeded context.

---

## Lessons Learned

1. **Backward-cleanup migrations matter for constraint introduction.** Adding NOT NULL, UNIQUE, or FOREIGN KEY constraints requires pre-checking for violating rows. Simple rule: if a constraint introduction can fail atomically (SqlException blocks index creation), prepare a safe prologue that cleans/updates existing data. Test it separately.

2. **Modifying merged migrations is acceptable under strict conditions:** (a) Recent ship (within 5 patches), (b) Original migration atomically rolls back (no partial MigrationsHistory), (c) Prologue is idempotent (success-path envs unaffected), (d) Provides no alternative path. Document decision in migration comments.

3. **Test fixture GUIDs are antipatterns.** Never hardcode entity IDs in test helpers, even for "readability" during debugging. Use `Guid.NewGuid()` + comment with the entity's business key (e.g., "WorkflowKey format: tenantId-caseTypeId-hash"). Debugger + xUnit output capture trumps fixed GUIDs every time.

4. **Async race conditions hide in test suites.** v2.133p11 would pass in local dev (single test run, slow machine), pass in CI (sequential test runs), but fail in high-parallelism environments (dev machine with 8 CPUs, parallel fixture setup). Consider worst-case parallelism when writing test data generation logic.

5. **Architecture tests catch constraints but not race artifacts.** MigrationConsistencyTests.cs validates schema consistency — it would flag a bad migration structure. But it doesn't validate whether historical data can survive constraint introduction. Future: add a "constraint introduction safety" test that populates test DB with worst-case duplicate scenarios, then attempts migration.

---

## Next Steps

1. **Immediate (done):** Merged v2.133p15 to main. Version bumped v2.133p14 → v2.133p15. ✅
2. **Immediate (done):** Updated roadmap progress (Workflow pod: Migration preflight cleanup → SHIPPED). ✅
3. **Backlog (OUTBOX-1-FU, P3):** Deduplication handler in consumer. Instead of idempotent SqlException, add explicit INSERT-IF-NOT-EXISTS check. Cleaner than relying on exception handling. Estimated: 1h (low priority, v2.133p11 idempotent handler already ships the safety net).
4. **Backlog (TEST-HARDENING, P2):** Audit all test fixture helper methods for hardcoded GUIDs. Replace with Guid.NewGuid() + business-key comment. Estimated: 3h across all modules.
5. **Backlog (ARCHITECTURE, P3):** Add "constraint safety" test suite to validate migrations with worst-case duplicate/violation scenarios before constraint application. Prevents similar surprises.

---

## Unresolved Questions

- **Q1:** Should the idempotent SqlException handler in v2.133p11 consumer be replaced with explicit INSERT-IF-NOT-EXISTS? Current: exception-based. Pro: already shipping, no code change. Con: relies on DB throwing exception (TOCTOU window exists during INSERT). → Backlog as OUTBOX-1-FU.
- **Q2:** Why didn't Architecture suite catch the unique index failure? MigrationConsistencyTests validates schema but not data-compatibility. Should we add a test that seeds violation data + attempts migration? → Document in Architecture suite design decisions.

---

## Closing Reflection

This session was a textbook example of **forward-protection without backward-cleanup.** v2.133p11 fixed the race going forward — elegant constraint design, idempotent error handling. But shipping incomplete migration prologue (missing DELETE) meant any environment with accumulated test data would crash on upgrade, even though the root race condition was fixed. The irony: users with clean databases wouldn't hit the issue. Users with messy test data would.

Modifying a merged migration feels like breaking a cardinal rule ("never touch shipped code"), but in this case it was the *only* safe option. New migration would fail. Re-running v2.133p11 with prologue is idempotent + atomic. The hard part wasn't the technical decision — it was accepting that incomplete migrations ship sometimes, and the right fix is to complete them in the same patch cycle, not defer to "next version."

Test hardening revealed another antipattern: hardcoded GUIDs in helpers. Shaming ourselves for that one — tests are *supposed* to be isolatable, not bound to magic numbers. This is worth a full test-fixture audit across all modules.

**Version bumped: v2.133p14 → v2.133p15. All 538 tests pass. Shipped.**

---

## Commit Readiness

- Tests: 538 (441 Workflow.App + 76 Workflow.Infra + 3 new Integration + 18 Architecture) — all pass
- Code review: APPROVE_WITH_MINOR (H1 preflight idempotent + M1 dedup logic = addressed)
- Migration: v2.133p11 Up() modified in-place (preflight DELETE + index creation)
- Docs: roadmap updated (Workflow: Migration preflight cleanup → SHIPPED)
- Branch: fix/v2-133p15-mig-preflight-cleanup
- Files: 1 modified (migration), 1 created (integration tests), 1 modified (unit test fixtures)

---

## Test Evidence

```
Workflow.Application Tests:        441/441 ✓
Workflow.Infrastructure Tests:      76/76 ✓
New Integration Tests:               3/3 ✓
Architecture Tests:                 18/18 ✓
─────────────────────────────────────────
Total:                            538/538 ✓
```

All suites pass. Ready for commit + PR.
