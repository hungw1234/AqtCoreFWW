# PHASE7-WF-SAVEGRAPH-IDENTITY: Production Blocker — Workflow Graph Save Failure

**Date:** 2026-05-08 15:00  
**Severity:** Critical  
**Component:** Workflow.Domain, Workflow.Infrastructure, Workflow.Application  
**Status:** Resolved  

---

## What Happened

Commit `2f6b6168` on `origin/main` — P1 production blocker fixed.

`PUT /api/v1/workflow/definitions/{id}/graph` returned HTTP 500 `SqlException: Cannot insert explicit value for identity column in table 'WorkflowStates' when IDENTITY_INSERT is set to OFF` for **all visual-designer save flows**. Bug manifested only at runtime during PHASE7-TIER-C-FU1-VERIFY testing 2026-05-08 (FE fixture fix 2026-05-07 unblocked the path).

---

## The Brutal Truth

**Missing test coverage masked a two-layer structural flaw.** PR-D2 (2026-05-07) retro *explicitly noted* the gap: "SaveDefinitionGraph lacks end-to-end integration test." We shipped without it, bet on static analysis, lost. The moment you run the endpoint, it explodes.

Original `ReplaceGraphAsync` raw-SQL was added "to avoid DbUpdateConcurrencyException" — solving a real pain point but **implementing it wrong**. Explicit `INSERT INTO WorkflowStates (Id, ...) VALUES ({s.Id}, ...)` rejects SQL Server IDENTITY column semantics. Simultaneously, handler built the FK map **before SaveChanges**, capturing `state.Id=0` for all transitions.

This is what happens when you defer integration tests and trust async-operations patterns you haven't actually validated end-to-end. The code smells right (raw SQL for performance, pre-build the map) until you run it.

---

## Technical Details

### Two-Layer Root Cause

**Layer 1: Raw-SQL IDENTITY violation**

File: `src/Modules/Workflow/Workflow.Infrastructure/Repositories/WorkflowDefinitionRepository.cs:ReplaceGraphAsync`

```csharp
// WRONG: SQL Server rejects explicit Id on IDENTITY column
await connection.ExecuteAsync(
  @"INSERT INTO WorkflowStates (Id, DefinitionId, Name, ...) 
    VALUES (@Id, @DefinitionId, @Name, ...)",
  new[] { s.Id, s.DefinitionId, s.Name, ... }
);
```

**Layer 2: Tracker corruption + premature FK resolution**

File: `src/Modules/Workflow/Workflow.Application/Commands/SaveDefinitionGraphCommandHandler.cs:40-65`

Handler captured state.Id **before** save:
```csharp
foreach (var s in command.States) {
  var state = new WorkflowState { ... };
  stateIdByName[s.Name] = state.Id;  // ← ZERO, not assigned yet
  context.WorkflowStates.Add(state);
}
// No SaveChanges here; continue building transitions with stateIdByName[*] = 0
```

Result: all `FromStateId` / `ToStateId` FKs = 0, even if IDENTITY insert succeeded.

---

## The Fix

Two-phase EF save mirroring `CreateWorkflowDefinitionCommandHandler` (proven pattern):

**Phase 1: State save with IDENTITY assignment**

```csharp
// Delete old states + transitions (FK order, raw SQL, bypasses tracker)
await connection.ExecuteAsync(
  "DELETE FROM [workflow].[WorkflowTransitions] WHERE DefinitionId = @Id; " +
  "DELETE FROM [workflow].[WorkflowStates] WHERE DefinitionId = @Id;",
  new { Id = aggregateId }
);

// Detach tracker orphans (prevents concurrency exception on re-add)
foreach (var orphan in context.ChangeTracker.Entries()
  .Where(e => e.Entity is WorkflowState or WorkflowTransition)
  .ToList()) {
  context.Entry(orphan.Entity).State = EntityState.Detached;
}

// Add states, save → IDENTITY assigns state.Id
foreach (var spec in command.States) {
  context.WorkflowStates.Add(new WorkflowState { Name = spec.Name, ... });
}
await context.SaveChangesAsync(txn);

// NOW build FK map from actual saved Ids
var stateIdByName = context.WorkflowStates
  .Where(s => s.DefinitionId == aggregateId)
  .ToDictionary(s => s.Name, s => s.Id);
```

**Phase 2: Transition save with resolved FKs**

```csharp
foreach (var spec in command.Transitions) {
  context.WorkflowTransitions.Add(new WorkflowTransition {
    FromStateId = stateIdByName[spec.FromStateName],  // ← actual Ids
    ToStateId = stateIdByName[spec.ToStateName],
    ...
  });
}
await context.SaveChangesAsync(txn);
```

**Wrapped in TransactionScope** with `AsyncFlowOption.Enabled` — closes PHASE4B-WF-FU1 side-effect.

Handler now accepts `IReadOnlyList<NewTransitionSpec>` (name-keyed transport record) instead of pre-built entities. Validation errors → `ValidationError` → HTTP 422 (FE parity with sibling handlers).

**Test audit assertions** (per advisor M3 flag):
- `ModifiedBy` + `UpdatedAt` populated post-save
- FK shape validated (no zero Ids)
- Idempotency verified (re-save same graph twice)

---

## Process Discipline That Worked

1. **Advisor pre-flight** surfaced tracker-pollution risk + TransactionScopeAsyncFlowOption requirement before code written.
2. **Proof-of-failure validation** via `git stash -u` on baseline — **reproduced exact IDENTITY_INSERT error**. Without this, test would pass blindly on fixed code; we verified it was actually red first.
3. **Code-review assertions** applied per M3 guidance (state.Id population, FK integrity).

---

## What This Revealed

**Hidden infrastructure debt:**

1. **`InlineWorkflowDefinitionFactory` has identical flaw** — captures `state.Id=0` before SaveChanges. Works only because no test asserts FK shape. Logged P3: PHASE7-WF-INLINE-FACTORY-FK-DRIFT (~30m).

2. **`WorkflowState.AutoTransitionOnTimeoutId` structurally broken** — long? FK to transition.Id that changes every save. No validation that timeout-transition exists. Logged P3: PHASE7-WF-AUTO-TRANSITION-ON-TIMEOUT-DRIFT (~1h).

3. **SaveDefinitionGraph test edge cases uncovered** — no coverage for re-save (state rename collision?), rollback (txn abort), concurrent saves. Logged P3: PHASE7-WF-SAVEGRAPH-TEST-EDGES (~2h).

---

## Test Results

- Workflow.Application unit: 429/429 ✅
- Architecture suite: 26/26 ✅
- New integration test: 1/1 ✅ (red on baseline, green post-fix)
- Code-review: APPROVE_WITH_CONCERNS 9.0/10; M1 + M4 applied pre-merge

---

## Closures

- **PHASE7-WF-SAVEGRAPH-IDENTITY** (P1) — primary
- **PHASE4B-WF-FU1** (P2) — TransactionScope wrap side-effect, bundled

---

## Spawned Backlog

- PHASE7-WF-INLINE-FACTORY-FK-DRIFT (P3, ~30m)
- PHASE7-WF-AUTO-TRANSITION-ON-TIMEOUT-DRIFT (P3, ~1h)
- PHASE7-WF-SAVEGRAPH-TEST-EDGES (P3, ~2h)

---

## Lessons Learned

1. **Integration tests for data-mutation endpoints are non-optional.** Async/EF patterns look correct in code review until you hit the actual RDBMS constraint. "We have unit tests" is not equivalent to "we have tested the persistence contract."

2. **Pre-SaveChanges FK resolution is a trap.** Always post-SaveChanges for IDENTITY columns. If you need the Id earlier, refactor to separate the concerns (validate structure, then persist).

3. **Raw-SQL for concurrency is valid; raw-SQL for IDENTITY insert is not.** Know your RDBMS semantics: explicit `(Id, ...)` on IDENTITY column = error. Use `INSERT ... OUTPUT inserted.Id` or EF SaveChanges if you need the assigned value.

4. **Proof-of-failure is cheaper than blind faith.** `git stash -u` → reproduce error on baseline → confirm fix works. 5 minutes, zero false-green risk.

5. **Test coverage gaps should be called out in retros + closed immediately, not deferred.** PR-D2 retro flagged this on 2026-05-07. We had 24h to write the test. We chose to ship without it. This is the cost.

---

## Next Steps

Backlog items from spawned debt:
- PHASE7-WF-INLINE-FACTORY-FK-DRIFT — audit factory pattern, mirror fix if needed
- PHASE7-WF-AUTO-TRANSITION-ON-TIMEOUT-DRIFT — revisit timeout-transition FK semantics (may need redesign)
- PHASE7-WF-SAVEGRAPH-TEST-EDGES — extend integration coverage to re-save, state rename, rollback scenarios
