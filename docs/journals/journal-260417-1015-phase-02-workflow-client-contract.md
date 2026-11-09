---
title: "Phase-02 Workflow Client Contract — Delegate Pattern + Permission-Set Authz"
date: 2026-04-17
branch: feat/phase-02-workflow-client-contract
plan: plans/260415-1737-cases-workflow-refactor/phase-02-extend-workflow-client-contract.md
status: complete
tags: [workflow, cases, contract, authz, refactor]
---

# Phase-02 — Extend IWorkflowModuleClient Contract

## What shipped

Extended `IWorkflowModuleClient` with 2 new methods + 4 contract types, migrated `ExecuteTransitionCommandHandler` authz from single-role equality to permission-set membership, and added a new `IIdentityModuleClient.GetEffectivePermissionCodesAsync` method. The cross-module client delegates to the existing handler via `IMediator` — zero duplicate transition logic.

### Surface changes

| Artifact | Change |
|---|---|
| `IIdentityModuleClient` | +`GetEffectivePermissionCodesAsync(userId, ct) → IReadOnlySet<string>` |
| `InProcessIdentityModuleClient` | Injects `IEffectivePermissionService`; delegates (Redis-cached, TTL 10min) |
| `IWorkflowModuleClient` | +`ExecuteTransitionAsync(request, ct) → ExecuteTransitionOutcome` +`QueryAllowedTransitionsAsync` + 4 record/class types |
| `InProcessWorkflowModuleClient` | Injects `IMediator` + `IIdentityModuleClient`; resolves (FromState, ActionName) → TransitionId, dispatches `ExecuteTransitionCommand` |
| `ExecuteTransitionCommand` | Dropped `string? ActorRoleCode` field |
| `ExecuteTransitionCommandHandler` | Injects `IIdentityModuleClient`; authz = `permissionCodes.Contains(transition.RequiredRoleCode)` |
| `BatchExecuteTransitionCommand` | Dropped `ActorRoleCode` (dead after handler change) |
| `WorkflowInstancesController` | REST DTOs retain `ActorRoleCode` for FE backward-compat; controller ignores value |

### Test coverage

- Workflow.Application: 400 tests pass (includes updated `ExecuteTransitionCommandHandlerTests` — 14 tests: 6 success, 5 failure, 3 permission-set variants)
- Workflow.Infrastructure: 56 tests pass (+8 new `InProcessWorkflowModuleClientTests`)
- Identity.Infrastructure: 22 tests pass (+2 new for `GetEffectivePermissionCodesAsync`)
- Integration: 1 new `ExecuteTransitionContractTests` smoke test against seeded `CaseProcess` definition — pass (31s cold-start testcontainer)

## Why delegate over re-implement

The original plan considered re-implementing transition logic inside `InProcessWorkflowModuleClient`. Red-team F01 flagged this as a duplicate-code trap — `ExecuteTransitionCommandHandler` already owns parallel-branch fan-out, `ConditionsJson` evaluation, attachment persistence, concurrency-check, and domain-event emission. The revised delegate pattern preserves all these behaviors for free and reduces the client to translation-only work: load instance + definition → resolve transition → dispatch → map result.

## Authz model shift

Before: `transition.RequiredRoleCode == request.ActorRoleCode` — exact role-equality check. Actor passed their own role string; handler compared. This model breaks the moment an actor has multiple roles (common), a role is renamed, or a permission belongs to several roles. Real-world cases fail.

After: `permissionCodes.Contains(transition.RequiredRoleCode)` — permission-set membership. Actor's effective permissions (direct + group + delegation roles → permission codes) resolved once per call via `IEffectivePermissionService`. The field name `RequiredRoleCode` is now semantically "required permission code" — rename deferred (Phase-02b D5) to avoid migration churn.

## Deferred to Phase-02b (backlog)

| ID | Item | Reason |
|---|---|---|
| D1 | Workflow definition cache (Redis-only per Phase-00 R1 precedent) | YAGNI in Phase-02; definitions are near-immutable |
| D2 | `WorkflowInstance.RowVersion` reinstate OR rename `UpdateWithConcurrencyCheckAsync` → `UpdateAsync` | Dead guard flagged by red-team F04 |
| D3 | OpenTelemetry spans + metrics on `ExecuteTransitionAsync` | Observability track |
| D4 | `TryResolveInstanceForEntityAsync` on client | Phase-03 needs — plan amendment |
| D5 | Rename `WorkflowTransition.RequiredRoleCode` → `RequiredPermissionCode` | Data migration + EF config |
| D6 (new) | Unify `GetAvailableTransitionsQuery.ActorRoleCode` filter with permission-set model | Currently still role-equality — inconsistent with handler |
| D7 (new) | Deprecate + remove `ActorRoleCode` from REST DTOs after FE migration | Kept for BC in Phase-02 |

## Surprises

- **FE coupling wider than expected**: `ActorRoleCode` appears in `workflow-types.ts`, generated OpenAPI schemas, i18n labels, e2e specs, `workflow-batch-action-toolbar.tsx`, `instance-detail-page.tsx`. Removing from REST DTOs would cascade into FE sprint — kept DTO field + deprecated in doc comments.
- **`BatchExecuteTransitionCommandValidator` already clean**: no `ActorRoleCode` rule existed (FluentValidation rules were never written for it). Tests, not validator, were the only callers.
- **`InProcessWorkflowModuleClient` DI**: `IMediator` was already wired by `WorkflowInfrastructureModule.AddMediatR` for query handlers — no new registration needed. But noted the cross-layer coupling (Infrastructure class consuming Application command via MediatR) as architecturally sound since the client acts as an anti-corruption layer.

## Follow-ups

1. Phase-03 can proceed — `IWorkflowModuleClient.ExecuteTransitionAsync` is the callable surface.
2. FE team owns `ActorRoleCode` deprecation cycle (D7).
3. Phase-02b must decide D2 before prod concurrency-safety claims.
