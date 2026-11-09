# Journal — WF Parallel-Branch Negative-Path Tests (premise audit + reframe)

**Date:** 2026-06-08
**Session:** `/cook P5-FOLLOWUP --auto` → tests-only slice (3 negative-path integration facts)
**Commits:** `21fd675a` test(workflow) + `cf8dbef8` docs (NOT pushed, ahead=2)
**Touched:** test project only — 0 production code.

## What

Added `WorkflowParallelBranchNegativeTests` (3 facts) covering branch pools that pass
design-time validation but diverge at runtime. Extended `ParallelBranchTestSeed`
(ChildPoolKind enum + empty-refs-continuation flag; extracted `SeedAndActivateAsync` +
`ForkInstanceAsync`). 10/10 green (3 new + 4 Runtime + 3 Concurrency, no regression).

## The real story — every premise was wrong

The backlog named three follow-ups (RT2, RT4-runtime-arm, plus the deferred RT13). Scout
against engine source refuted all three runtime premises BEFORE any test was written:

- **Resolver is a predicate-pool producer.** `WorkflowHandlerResolver.ApplyClauseAsync`
  pushes a `CandidatePredicate` for ByPermission/ByRole **regardless of membership**. It
  never enumerates users. So `HandlerResolution.IsEmpty` is false for ANY non-empty clause.
- ⇒ The checkpoint's "0-member ByRole child → fork-entry 422" was wrong: a 0-member role
  forks **successfully** (non-empty predicate). The pool is only Empty via an empty refs
  array (`RefsJson="[]"`), which RB5 lets through (it checks `Clauses.Count`, not refs).
- ⇒ RT2's "empty continuation → clean rollback" was wrong: child resolutions commit
  separately (sibling visibility), so a failed join leaves a **strand**, not a rollback.

Reframed and tested actual behavior:
1. **empty-refs child** → fork-entry fail-CLOSED 422, instance at Draft, no branch.
2. **empty-refs continuation** → join 422 + branch InProgress / all-children-Approved /
   instance-not-advanced = a strand (H1 reconciler territory).
3. **unheld-role child** → fork SUCCEEDS, child unclaimable (403 on claim) → AND-join
   deadlock. This is the genuinely-reachable, untested risk the checkpoint mislabeled.

## Lessons

- **EF/resolver gotcha (reusable):** a predicate-pool resolver that pushes a clause
  predicate without enumerating members means "0 members" ≠ "empty pool". Empty is only
  reachable via empty/blank refs or a contributor that adds nobody (PreviousHandler with no
  prior task). Design-time "a clause exists" guards do NOT prevent runtime unclaimable pools.
- **Audit-vs-engine discipline paid off:** treating the backlog/checkpoint text as
  hypotheses and verifying each against source (resolver + validator + claim handler)
  turned a "write 3 tests" task into "3 tests asserting truth + 1 real gap surfaced +
  1 mischaracterized finding corrected." Advisor flagged the empty-refs reachability hole
  (RB5 count-only) that made the guards testable rather than dead code.
- **Non-vacuity via nullability:** the strand test's `ResolutionType == Approved` guard has
  teeth despite `Approved == 0`, because `ResolutionType` is nullable — a rolled-back child
  would read `null != Approved` and fail the assertion.

## Carry-forward

- Deferred prod-fix carved out → backlog `WF-PARALLEL-RB5-RESOLVE-TO-CLAIMANT` (RB5 should
  reject a child pool resolving to 0 claimants at activation; high-risk, not prod-reachable
  yet so not urgent; pairs with H1 as a runtime backstop for post-activation drift).
- Still open in the follow-up: H1 reconciler, RT13 (BP ambient-tx into a branching target —
  reachable via `InProcessWorkflowModuleClient.ExecuteTransitionAsync`, deferred = heavier
  BP harness).
- Not pushed (user decides).
