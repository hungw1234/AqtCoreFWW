# Phase 02 Cases Workflow Finalize v2 — WorkflowDefinition Validation + RBAC Hardening

**Date**: 2026-04-22 11:10
**Severity**: High (RBAC refactor)
**Component**: Workflow.Application + Workflow.Infrastructure + workflow admin FE
**Status**: Resolved

## What Happened

Phase 02 shipped: `WorkflowDefinition` entity expanded with `EntityType` field (`Case` default) + EF migration (Designer+Snapshot generated correctly) + state/transition name validation via `IEntityStatusEnumRegistry` interface + RBAC hardening on `WorkflowDefinitionsController` (native ASP.NET AND policy stack: class-level View decorator + method-level Manage for writes) + audit logging wired via existing `AuditBehavior` MediatR pipeline (9 commands converted to `ICommand<T>` marker interface) + FE mirror (readonly hidden entityType field in form). 36 new tests: 23 unit + 13 integration across 5 test files.

## The Brutal Truth

This phase moved fast — 3h actual vs 1.3-1.5d estimate. Feels like a win, but the speed exposed a process risk: **we were gambling on pattern reuse without verification**. Had `AuditBehavior` not existed or had the AND policy evaluation worked differently, we'd have shipped RBAC silently broken. Scout saved us. Going forward, RBAC swaps → empirical test of policy AND/OR logic before code review. No assumptions on ASP.NET behavior.

The migration worked correctly on first try (Designer+Snapshot generated clean), which usually means we got lucky — in 5 previous phases, migrations had naming errors or snapshot drift. That luck will run out; we need explicit Designer validation in CI.

## Technical Details

**EntityStatusEnumRegistry DDD pattern:**
```
IEntityStatusEnumRegistry.GetAllowedStates("Case") → ["Draft", "Active", "Closed"]
IEntityStatusEnumRegistry.GetAllowedTransitions("Case", "Draft") → ["Active"]
```
Impl wired via DI in `WorkflowApplication` module; used by 2 validators (`CreateWorkflowCommandValidator`, `UpdateStateCommandValidator`).

**RBAC AND stack:**
```csharp
[Authorize(Policy = PermCodes.Workflow.Def.View)]  // Class-level
public class WorkflowDefinitionsController : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = PermCodes.Workflow.Def.Manage)]  // Method-level
    public async Task<IActionResult> CreateAsync(...) // Requires BOTH View AND Manage
}
```
Test `UserWithOnlyManage_CannotListDefinitions_Returns403` explicitly verifies AND (not OR). If ASP.NET had applied OR, attacker with Manage-only token could enumerate all definitions. Critical gap caught by test, not code review.

**AuditBehavior wiring:**
`CreateWorkflowCommand` → added `ICommand<WorkflowDefinitionDto>` marker → `AuditBehavior<CreateWorkflowCommand, WorkflowDefinitionDto>` auto-pipes. No inline `IAuditLogger.LogAsync` calls; DRY win vs plan's original inline pattern. 9 commands converted.

**Migration single-step vs 3-step trade-off:**
Plan suggested "nullable → backfill → NOT NULL" 3-step for prod safety (avoid table lock). Subagent emitted: `AddColumn(nullable: false, defaultValue: "Case")` single-step. SQL Server backfills in one statement. Acceptable for non-prod; prod migrations will need human 3-step (deferred to ops).

## What We Tried

1. Plan v1 assumed 1.5-2.5d (2 fullstack devs, migration complexity, RBAC retesting). Scout revealed `PermCodes.Workflow.Def.View` existed + `AuditBehavior` pipeline existed → re-estimated 1.3-1.5d → actual 3h.
2. Trust-but-verify after each 4-person Group commit: Grep sampled test paths (revealed one path mismatch: `tests/unit/` not `src/tests/`), migration Designer artifact signature, audit command marker. No drift found after manual fix.

## Root Cause Analysis

**Why phase went fast:** Reusable patterns were already in codebase (AuditBehavior, View/Manage policies, EntityStatusEnumRegistry contract from Phase 01 port). Scout-first prevented re-implementing. Investment in Phase 01 architecture paid off.

**Why RBAC felt risky:** ASP.NET policy stack behavior (AND vs OR for stacked Authorize) is implicit. No test explicitly asserts the evaluation order. Dev team assumption validated by test, not documentation. Fragile.

**Why migration was clean:** Designer+Snapshot snapshot task was explicit in plan; subagents executed checklist. No ad-hoc SQL. Mitigated common drift.

## Lessons Learned

1. **Scout-first for phases following architectural patterns.** Plan v1 estimated 2.5d; scout 30min revealed 70% reuse → actual 3h. Before assigning fullstack devs, run 30min scout on "does this pattern exist already?" for any infrastructure-heavy phase. ROI: 2.5d vs 2.5h.

2. **Empirical RBAC AND/OR assertion is not optional.** ASP.NET stacks Authorize attributes via AND by default (both must pass), but it's policy engine magic, not documented in code. Added test `UserWithOnlyManage_CannotListDefinitions_Returns403` to lock this behavior. Future RBAC swaps: empirical test before code review.

3. **SeederCasingTest (2-assertion unit file) is a permanent guard.** Asserts (a) seeded workflow states ⊇ enum names, (b) char-by-char equality. Detects casing drift in both directions, runs every CI. Low cost, high fidelity.

4. **DRY refactor (inline → AuditBehavior pipeline) ≠ scope creep if it eliminates duplication.** Plan said "mirror" (copy pattern), discovery said "existing pipeline exists" → used marker interface + DI instead. Code review approved as DRY win. Signals: plan is a hypothesis, scout updates hypothesis before execution.

5. **Lookup tenant filter is hygiene debt, not blocker.** `WorkflowDefinitionEntityTypeLookup` has no tenant filter — attacker with valid GUID could probe existence + type. Handler-level tenant check prevents data leak. Deferred as follow-up; acceptable for Phase 02 scope.

## Next Steps

1. **Prod migrations require manual 3-step:** When Phase 02 is deployed to prod, ops team must execute "nullable → backfill → NOT NULL" using `sp_rename` + separate statement (avoid table lock). Document in deployment runbook.

2. **Designer validation in CI:** EF migrations with Designer+Snapshot need explicit CI check (not just build success) to catch snapshot drift early. Low-priority follow-up.

3. **Lookup tenant filter follow-up:** File ticket for `WorkflowDefinitionEntityTypeLookup` to add tenant filter — defense-in-depth, not critical path.

4. **AuditBehavior resourceId propagation (Phase 03):** Current `AuditBehavior` wires `UserId` + `TenantId` but omits `resourceId` (e.g., WorkflowDefinitionId). Phase 03 should enrich signature to support optional `IResourceIdentified` marker.

5. **Test project dependency hygiene refactor:** `AqtCoreFW.Workflow.Application.Tests` → Infrastructure to construct `EntityStatusEnumRegistry`. Create `FakeEntityStatusEnumRegistry` in test proj instead. Low-priority layering cleanup.

