---
title: "Phase-03 Cases → Workflow Delegation — Feature-Flag Gated"
date: 2026-04-17
branch: feat/phase-03-wire-cases-workflow
plan: plans/260415-1737-cases-workflow-refactor/phase-03-wire-cases-commands-to-workflow.md
status: complete
tags: [cases, workflow, contract, refactor, feature-flag]
---

# Phase-03 — Wire Cases Command Handlers to Workflow Client

## What shipped

5 Case transition handlers (Submit/Assign/Approve/Reject/Close) now delegate state machine to `IWorkflowModuleClient.ExecuteTransitionAsync` when feature flag `feature:cases.workflow-delegation` is ON. Flag OFF → legacy `CaseWorkflow.Engine` path (rollback safety). `CreateCaseCommandHandler` creates the bound `WorkflowInstance` when flag is ON. Contract extended with 2 new methods. `WorkflowErrorCodes` relocated to `SharedKernel.Contracts` for cross-module consumption without a Workflow.Domain reference.

### Contract additions

| Method | Purpose |
|---|---|
| `IWorkflowModuleClient.CreateInstanceByDefinitionKeyAsync(key, entityType, entityId, tenantId, createdBy)` | Resolve latest definition version by key — callers avoid holding raw Guid |
| `IWorkflowModuleClient.TryResolveInstanceForEntityAsync(entityType, entityId, tenantId)` | Tenant-scoped active-instance lookup. Logs `CaseWorkflowInstanceDuplicate` + picks `StartedAt ASC` if 2+ active (DB unique index deferred to Phase-05) |

### Code churn

- `WorkflowErrorCodes` moved: `Workflow.Domain.Errors` → `SharedKernel.Contracts.Clients`. `Workflow.Domain` adds ref to `SharedKernel.Contracts`.
- `Case` entity: 5 new `Record*` methods accept `newStateName` from outcome + `ParseStatus` drift guard (throws `InvalidOperationException` on unknown state).
- `CaseTransitionedEvent` added with signature matching Phase-04 plan (`CaseId, TenantId, FromStatus, ToStatus, ActorId`). `Record*` methods raise rich per-action event + generic transition event.
- 5 handlers + `CreateCaseCommandHandler` inject `IFeatureFlagService` (sync API, tenant-scoped).
- `CaseTransitionHelpers` static helper — `MapTransitionError` + `SaveWithConcurrencyGuardAsync` — shared by 5 handlers (DRY, rule-of-5 rather than rule-of-3).
- REST DTOs unchanged.

### Test coverage (32 pass total)

- Cases.Application: 32/32
  - `CreateCaseCommandHandlerTests` — 9 existing + 3 new (workflow call, call order, flag-off skip)
  - `TransitionHandlersTests` new — 20 tests: 5 flag-on happy + 5 flag-off legacy + 4 error-code matrix (Submit) + 1 instance-missing + 1 state-drift guard
- Workflow.Infrastructure: 61/61 (+5 new: TryResolve × 3 + CreateByKey × 2)
- Workflow.Application: 400/400 (no regression)
- Integration: Phase-02 smoke test still green

## Reviewer round 2 — 2 HIGH defects caught and fixed

Code-reviewer APPROVE_WITH_MINOR with 2 HIGHs flagged pre-merge:

- **H1 — Flag asymmetry on CreateCaseCommandHandler.** First pass created WorkflowInstance unconditionally. Reviewer mapped 3 failure scenarios (flag OFF wastes rows; OFF→ON leaves pre-existing orphans; ON→OFF drifts Case.Status from live instances). Fix applied: CreateCase gated by same flag. Flag symmetry restored.
- **H2 — `DbUpdateConcurrencyException` catch removal was premature.** Original reasoning invoked D2 (dead `WorkflowInstance.UpdateWithConcurrencyCheckAsync`). But `Case.RowVersion` is LIVE — wired in `CaseConfiguration:50-52` + migration `20260320030814_AddRowVersionToCaseEntity`. D2 only applies to Workflow module's dead guard, NOT Cases. Removing the catch would convert concurrent updates from 409 → unhandled 500s. Fix: restored catch; extracted to `CaseTransitionHelpers.SaveWithConcurrencyGuardAsync`.

### Also addressed
- **M1:** Added `GOV_WFL_001 DefinitionNotFound` case to `MapTransitionError` with clear message "Quy trình CaseProcess chưa được cấu hình..."
- **M2:** Extracted `MapTransitionError` + `SaveWithConcurrencyGuard` to shared helper (5 × 20 LOC → 1 × 40 LOC). Rule-of-5 beats rule-of-3 when touches are already happening.

## Design decisions locked this session

- **Feature flag convention:** `feature:cases.workflow-delegation` (SYNC API, tenant-scoped). Default OFF via DB-row absence; ops enables by inserting `SystemParameter`. 4 other callers in repo follow same `feature:*` prefix.
- **Workflow is authority on target state:** `Record*` methods accept `newStateName` from `ExecuteTransitionOutcome.Result.NewStateName` — no hardcoded enum. Drift surfaces as exception, not silent corruption.
- **Rich events preserved:** `CaseSubmittedEvent(actor)`, `CaseAssignedEvent(assignee+dept)`, `CaseApprovedEvent(reason)`, etc. retained alongside generic `CaseTransitionedEvent`. Notifications subscribers unchanged. Phase-04 can swap to generic after migration without breaking current pipeline.
- **Flag symmetry across Create + Transitions:** avoids 3 orphan/drift scenarios enumerated by reviewer H1.
- **`WorkflowErrorCodes` lives in Contracts, not Domain:** error codes ARE the cross-module wire contract. Workflow.Domain now refs SharedKernel.Contracts for its factory methods.

## Deferred / follow-ups

| ID | Item | Why |
|---|---|---|
| Open Q #1 | Phase-05 DB unique partial index on `workflow.WorkflowInstances` for `(EntityType, EntityId, TenantId) WHERE CompletedAt IS NULL` + dedupe backfill | Data-integrity belt-and-braces; Phase-03 uses log-warning + FirstOrDefault by StartedAt |
| Reviewer L1 | `ParseStatus` case-sensitive — recommend Phase-05 validator | Seeder writes exact names, low risk in practice |
| Reviewer L2 | `CreateCaseCommandHandler` orphan-on-infra-failure (Case saved but WorkflowInstance creation throws) | Runbook + SQL drift monitor from Phase-04 (`SELECT c.Id FROM Cases c LEFT JOIN WorkflowInstances ...`) sufficient |
| Phase-02b D6 | `GetAvailableTransitionsQueryHandler` authz model unification | Still role-equality, inconsistent with permission-set handler |
| Phase-02b D7 | Remove `ActorRoleCode` from REST DTOs after FE migration | Kept for BC, server ignores |

## Stacked PR coordination

Phase-03 branch (`feat/phase-03-wire-cases-workflow`) stacked off Phase-02 PR #28 (still open). On Phase-02 merge to main, Phase-03 needs a trivial rebase (1 commit ahead). PR body notes dependency.

## Surprises

- **Advisor caught 2 HIGHs that reviewer then confirmed** — code-reviewer round 2 was worth the time, would have shipped broken concurrency behavior + orphan-scenarios without it.
- **Feature flag API is sync not async** — 4 existing callers confirmed before we wrote anything. Would have failed compile if we'd trusted plan's `IsEnabledAsync` wording.
- **`Case.RowVersion` vs `WorkflowInstance.RowVersion` asymmetry** — only Workflow side was dropped (Phase-02b D2). Cases module's concurrency guard must stay until Cases ever drops it (not planned).
