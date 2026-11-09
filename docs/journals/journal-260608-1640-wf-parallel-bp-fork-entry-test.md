# Journal — WF Parallel-Branch BP Fork-Entry Test (cross-module UoW)

**Date:** 2026-06-08
**Session:** `/cook P5-FOLLOWUP --auto` → tests-only (1 integration fact)
**Commits:** `a258289b` test(businessprocess) + `282c888a` docs (NOT pushed, ahead=2)
**Touched:** test project only — 0 production code.

## What

Added `ExecuteBpTransitionTransactionTests.ExecuteBusinessProcessTransition_IntoBranchingState_StagesForkUnderCrossModuleUow` — a business-process run transition whose target is a parallel-branch fork-entry state. Closes the gap where the BP transition suite deliberately excluded branch tasks, so the fork-entry-under-ambient-UoW path was never exercised. 4/4 green (1 new + 3 existing, no regression).

Mechanism proven: `ExecuteBusinessProcessTransitionCommandHandler` runs `uow.BeginAsync` → `IWorkflowModuleClient.ExecuteTransitionAsync` (→ same `ExecuteTransitionCommandHandler` fork-entry → `InitiateBranchingAsync` stages with no internal SaveChanges) → `uow.CommitAsync`. The fork (branch InProgress + 2 child statuses + 2 claimable tasks) commits with the BP transition and the run advances, with no DTC promotion.

## Lesson — calibrate the claim to what the harness can prove

Code-review's sharp catch: my first comment + name (`…StagesForkAtomically`) overstated. The cross-module UoW is a shared `SqlConnection` + `SqlTransaction`, **not** a `TransactionScope`. So a post-commit read cannot distinguish "fork enlisted in the BP tx" from "fork committed on a separate auto-committed connection" — both leave identical rows. And there's no DTC machinery on this path to fail loudly, so the 200 carries near-zero atomicity signal for the fork specifically.

What the test honestly proves: the SUCCESS/commit path (fork staged + committed + run advanced, no promotion). True rollback atomicity (fork artifacts absent if a later same-UoW write fails) needs fault injection — an untested companion gap. Renamed to `…StagesForkUnderCrossModuleUow` and re-scoped the comment, mirroring the file's existing convention of pointing atomicity coverage elsewhere.

Reusable: when a test's setup lacks the machinery that would make the stronger property observable (here: no `TransactionScope`/DTC to fail on non-enlistment), name and comment it for what the assertions actually discriminate — not the property you wish you were proving.

## Carry-forward

- P5-FOLLOWUP now has **only H1 reconciler** open (`BranchTimeoutCheckerJob` ext: InProgress-branch + all-children-resolved + instance-not-advanced → complete join; covers timeout-strand + transient + the empty-continuation config-strand). High-risk new prod component; path not prod-reachable yet (0 parallel defs).
- Deferred prod-fix `WF-PARALLEL-RB5-RESOLVE-TO-CLAIMANT` still open (RB5 reject 0-claimant pool at activation).
- Optional fault-injection follow-up: BP fork rollback atomicity (this test's companion gap).
- Not pushed (user decides).
