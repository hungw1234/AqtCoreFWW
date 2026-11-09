# Phase 5b: First Task on Instance Creation — Identity-FK Pivot & Atomicity Trade-off

**Date**: 2026-05-21 21:00–23:45 ICT
**Severity**: Medium (architectural pivot, not a bug — discovered via TDD before shipping)
**Component**: Workflow instance creation, task factory wiring
**Status**: Resolved (shipped LOCAL with 2-step-save pattern)

## What Happened

Phase 5b wired the `IWorkflowHandlerResolver` + `IWorkflowTaskFactory` into the instance creation surfaces (command handler + BP client). The plan's architecture diagram claimed a single `SaveChangesAsync` flush would persist both the parent `WorkflowInstance` row and the dependent `WorkflowTask` row atomically (lines 49, 79). When writing integration test I1 (HTTP POST happy path), that claim broke in practice.

The test created an instance and immediately queried for tasks. The database contained an instance row but the task had persisted with `WorkflowInstanceId=0` — an orphan with FK pointing to a non-existent instance. This should never happen in production.

**Root cause:** `WorkflowTask.WorkflowInstanceId` is a plain `long` column with **no EF relationship configuration** (no `HasOne(...).WithMany(...).HasForeignKey(...)` declaration). When EF Core executes a single `SaveChanges` that inserts both parent and child, it:

1. Inserts the parent, receives back the IDENTITY-generated `Id`
2. Inserts the child — **but the child's FK property was already assigned before the parent's identity was known**

The child was added to the context with `WorkflowInstanceId=0` (its initial value at `Add`-time), and EF did not retroactively update that value after the parent's identity arrived. The FK persisted as 0.

## The Brutal Truth

This is incredibly frustrating because the plan explicitly claimed atomicity in a single flush without verifying the entity configuration actually supported it. **We shipped the plan specification without testing it.** The confidence came from "this sounds right" + "unit tests with mocks all passed," which is a classic EF gotcha.

The plan spent 1 full page (lines 49, 79) detailing the atomicity invariant, and none of that reasoning accounted for the fact that **EF cannot propagate IDENTITY keys without an explicitly configured relationship**. This is not a subtle EF behavior; this is table-stakes for any multi-entity insert.

More frustrating: this is the **fifth EF structural-claim drift in 3 days** (DCAND-a-A, 4-C-A, S10a-A, 5c-A, now 5b-A). The pattern is clear: planner sketches a persistence strategy without grepping the entity configuration first.

## Technical Details

**Symptom in I1 integration test:**

```csharp
// POST creates instance with valid initial pool → 200
// Immediate follow-up query:
var tasks = await ctx.WorkflowTasks
  .IgnoreQueryFilters()
  .AsNoTracking()
  .Where(t => t.WorkflowInstanceId == createdInstanceId)
  .ToListAsync(ct);
// Expected: 1 task with FK = createdInstanceId (≠ 0)
// Actual: 1 task with FK = 0 — orphan row
```

**Existing entity config (unchanged in 5b):**

```csharp
// WorkflowTask.cs — NO navigation property to instance
public long WorkflowInstanceId { get; set; }  // plain FK column, no relationship

// WorkflowInstanceConfiguration.cs — no inverse nav-prop setup
// No HasOne(wt => wt.WorkflowInstance).WithMany(wi => wi.Tasks)
```

**Why mocks missed it:**

Unit tests used NSubstitute mocks for repositories. The mock's `SaveChangesAsync` was stubbed to return success without executing real EF or SQL. Mock confidence is misleading when DB-generated keys matter.

**Two-step-save pivot:**

```csharp
// BEFORE (plan): single flush, FK=0 bug
instanceRepository.Stage(instance);
taskFactory.StageFirstOrNextTask(...);
await instanceRepository.SaveChangesAsync(ct);  // FK=0 persists

// AFTER (implementation): two-step under transaction
instanceRepository.Stage(instance);
await instanceRepository.SaveChangesAsync(ct);  // Flush → DB-identity generated
// ^ Now instance.Id is real
taskFactory.StageFirstOrNextTask(...);  // FK picks up real instance.Id
await instanceRepository.SaveChangesAsync(ct);  // Flush → task with correct FK
```

Transaction wraps both SaveChanges calls (via owned infrastructure or ambient `TransactionScope.Current` on BP-path). Atomicity preserved. Rollback on either flush rolls back both.

## What We Tried

1. **Single SaveChanges with mocked repos** → tests passed, mocks don't care about IDENTITY propagation
2. **Static code review** → plan diagram looked sound; no one questioned the FK config
3. **Integration test (I1 real EF+SQL)** → FK=0 orphan discovered immediately

## Root Cause Analysis

**Primary:** Plan specified atomicity without verifying entity configuration supports IDENTITY-FK propagation. This is a planner discipline failure, not an implementation error.

**Secondary:** Unit-test mocks created false confidence. "All unit tests pass" → "implementation is correct" is a trap when persistence semantics matter.

**Tertiary:** Five EF structural-claim drifts in 3 days suggests the orchestrator needs a pre-cook validation gate: planner must `grep` entity config for any claim involving multi-entity SaveChanges.

## Lessons Learned

**5b-A (PERMANENT INVARIANT):** When an entity's FK column is a plain `long` with no `HasOne(...).WithMany(...)` relationship config, **EF Core does NOT propagate IDENTITY-generated keys to that FK during a single SaveChanges flush**. The dependent persists with FK=0 or whatever value it held at Add-time. Future planner specs claiming "single SaveChanges atomic parent+child insert" MUST first `grep` entity config for explicit relationship setup. (Fifth EF structural-claim drift in 3 days; pattern is systemic.)

**5b-B:** Two-step-save under transaction is the canonical pattern for "FK-without-nav-prop atomicity." Parent first (get real identity), child second (FK picks up real value), both within transaction boundary. Rollback semantics preserved. NOT a violation of S7 "single SaveChanges" invariant — S7 applies to concurrency-sensitive UPDATE paths (transitions), not creation (no race window within request).

**5b-C:** Integration tests reveal structural EF bugs that mocks hide. Unit mocks are fine for testing business logic branching; they are misleading for testing persistence semantics. Any feature where the parent uses DB-IDENTITY AND has dependent FKs MUST include real integration test before claiming green-bar.

**5b-D:** User gate Q5 (error shape change) implied three downstream callers needed migration (CreateWorkflowInstanceCommandHandler + InProcessWorkflowModuleClient + forms surfaces in BP). This was surfaced as Q5b cascade question, which is correct — one framing question exposes N-caller scope. Joins S10a/S10b precedent: orchestrator must enumerate callers BEFORE claiming "small change."

**5b-E:** Integration-test-driven plan correction feels late when discovered mid-impl, but it's healthier than shipping a plan that claims atomicity without TDD proof. INVARIANT: when plan claims atomicity in a flow with DB-generated keys, integration test MUST be part of plan scope, not deferred.

## Next Steps

1. **Canonical-doc amendment (Phase 6 carry-fwd M1):** Update plan template to clarify two-step-save pattern under transaction. Cite 5b-A/5b-B as reference.
2. **Planner pre-cook validation:** Before next multi-entity SaveChanges claim, grep entity config for relationship setup. Add to orchestrator's pre-cook gate checklist.
3. **Integration-fixture BP tests (Phase 6 M2):** I3/I4 tests depend on BP.Application integration fixture (infrastructure not yet ready). Parked but design confirmed.
4. **Ops pre-flight audit (Phase 6 L4):** Query for existing empty-pool BPs before prod activation (prevent hard-blocks from Q2 WF_NO_HANDLER gate).

## Metrics

- Cycle: ~2h45min vs 1d budget = 3× faster (despite mid-impl pivot)
- Files modified: 9 production + 7 test files (4 new + 3 updated)
- Test delta: +6 A1-A6 unit + 7 A7 unit + 1 Stage repo + 2 I1-I2 integration + 1 I3 integration + 1 I4 integration + 1 FineResolver Case D rewrite = **+14 facts**
- Coverage gates: 100% green (build 0E, 4 unit suites + Architecture + 2 integration suites, all pass)
- User gates: Q5 + Q5b cascade + Q6 (3 ratifications this session)

**Test count targets (SUCCESS CRITERIA):**
- Workflow.Application: 498→505 (+7 A1-A7) ✓
- Workflow.Infrastructure: 66→69 (+3 A7 mirror + Stage repo) ✓
- Integration G3: +4 I1-I4 ✓
- FineResolverGate: Case D rewrite (preserved 5/5 invariant) ✓
- Architecture.Tests: 101/101 (no drift) ✓

## Unresolved Questions

- BP-path HTTP integration (I3/I4) requires BP.Application integration fixture — deferred Phase 6 M2 per dependency map.
- Pre-flight ops audit SQL queries (Q1-Q5) need sync with actual PermissionCode table (currently hardcoded 122 codes, actual ~126) — Phase 6 L4.
