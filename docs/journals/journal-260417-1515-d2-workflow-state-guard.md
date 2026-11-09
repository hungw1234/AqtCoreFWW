---
title: "D2 HIGH — Dead Concurrency Guard Reinstated via EF Native Token"
date: 2026-04-17
branch: feat/phase-03-wire-cases-workflow
plan: plans/260415-1737-cases-workflow-refactor/phase-03-wire-cases-workflow.md
status: complete
tags: [workflow, concurrency, state-guard, cases, refactor, prod-blocker]
---

# D2 — Reinstate Concurrency Guard on WorkflowInstance State

## What shipped

Closed production-blocking tech debt (D2 HIGH): dead concurrency check in `UpdateWithConcurrencyCheckAsync` method replaced with EF native `IsConcurrencyToken()` on scalar `WorkflowInstance.CurrentStateId` column. Defense-in-depth strategy layers app-level transition validation with DB-level conflict detection. New error code `GOV_WFL_013` (StateConflict) and integration tests across both layers.

### Surface changes

| Artifact | Change |
|---|---|
| `WorkflowInstance.CurrentStateId` | +`.IsConcurrencyToken()` (EF metadata, no schema change) |
| `UpdateWithConcurrencyCheckAsync` | Renamed → `UpdateWithStateGuardAsync` (avoid collision with base `IRepository.UpdateAsync`) |
| `ConflictError` (new) | Shared-kernel error type + `WorkflowErrors.StateConflict()` factory method |
| `ExecuteTransitionOutcome` | +`Conflict(WorkflowErrorCodes.StateConflict)` branch |
| `ExecuteTransitionCommandHandler` | Wrapped `_workflowInstanceRepository.UpdateWithStateGuardAsync` in try/catch → `DbUpdateConcurrencyException` → `ExecuteTransitionOutcome.Conflict` |
| `InProcessWorkflowModuleClient` | Added `ExtractWorkflowErrorCode` helper; routes `StateConflict` via metadata to Cases caller |
| `SlaBreachCheckerJob` | Per-instance SaveChangesAsync with concurrency-exception catch+detach (batch save no longer poisons Hangfire on conflict) |
| `MigrationBatchProcessor` | Added `SaveWithConcurrencyHandlingAsync`; logs + detaches + rethrows for diagnostic visibility |
| `CaseTransitionHelpers.MapTransitionError` | +StateConflict switch arm w/ Vietnamese message |

### Test coverage

- Workflow.Application: 400 tests pass (updated handler tests + new StateConflict scenarios)
- Workflow.Infrastructure: 61 tests pass (+new concurrency integration test)
- Cases.Application: 32 tests pass
- Integration (new): 2 new `UpdateWithStateGuardAsync` tests (sequential race → InvalidTransition; simultaneous → StateConflict)
- **Total:** 497 tests pass, 0 failures

**Metrics:** 29 files changed (+893/-49), 0 schema migrations, commit `48a3dea3`, PR #30

## The critical finding (tester missed, code-reviewer caught)

Code-reviewer flagged F1 Critical: when creating `new ConflictError(WorkflowErrorCodes.StateConflict)`, the error **lacked metadata** expected by `InProcessWorkflowModuleClient.ExtractWorkflowErrorCode`. Without metadata, caller silently fell back to `InvalidTransition`, and Cases module never saw StateConflict. All 497 unit+integration tests still passed because they used mocks, not real cross-module flow.

**Lesson:** Tester validates code compiles and runs. Code-reviewer validates code honors its contract with the rest of the system. Both are non-negotiable.

### Secondary findings fixed in-session

| Finding | Severity | Fix |
|---|---|---|
| F2 | High | Integration test accepted fallback (either StateConflict OR InvalidTransition); tightened to `stateConflictCount >= 1` across 5 attempts |
| F3 | High | `SlaBreachCheckerJob` batch SaveChangesAsync could poison Hangfire on conflict; split to per-instance save w/ exception catch+detach |
| F4 | High | `MigrationBatchProcessor.SaveWithConcurrencyHandlingAsync` added diagnostic logging + rethrow for Hangfire retry visibility |
| F7 | Medium | `CaseTransitionHelpers.MapTransitionError` missing StateConflict branch (Vietnamese UX) |

## Why this matters

**App-layer check alone is insufficient.** Under READ COMMITTED isolation, two concurrent `ExecuteTransitionCommand` dispatches can both pass the `FromStateId == instance.CurrentStateId` check, then race to SaveChanges. EF's concurrency token detects this at DB write time and throws — catching this and returning `StateConflict` tells the caller "your state changed mid-flight; retry with fresh data."

Decision matrix preferred option (b) — EF native `.IsConcurrencyToken()` on scalar column — avoids the bug that forced original RowVersion removal (commit 33d4973f, column type mismatch in migrations).

## Deferred

None. D2 fully closed; no D2b opened.

## Reflection

1. **Red-team advisor caught simultaneous-race gap** in planner stage (before code). Had I implemented without plan review, bug ships.
2. **"Factory pattern for shared-kernel errors is non-negotiable"** — added to code standards. Every enum addition (WorkflowErrorCodes) must have corresponding factory method (WorkflowErrors.XXX()).
3. **Layered defense (app + DB) validated.** Sequential races fail fast at app layer (invalid state); simultaneous races caught at DB layer (token conflict). Both paths logged + observable.

Version bumped v2.107 → v2.108. Backlog D2 (HIGH prod-blocker) marked Done.
