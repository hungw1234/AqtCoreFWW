# Phase 3 Return Transition + Admin Tenant Fallback Hardening

**Date**: 2026-04-21 11:45
**Severity**: Medium (shipped, user confusion resolved)
**Component**: Cases.Workflow module, Admin query handlers
**Status**: Resolved

## What Happened

Started with Phase 2 shipped (feature flag enabled for dev tenant). User reported buttons on case detail weren't reflecting available workflow transitions. Launched Phase 3 (Return transition) + discovered & fixed critical tenant isolation bug in workflow admin queries after user reported workflow templates hidden from admin UI.

## The Brutal Truth

Two things happened:
1. **Dead code pipe**: Return transition existed in the workflow definition but no endpoint/handler/FE hook wired it. Phase 3 implementation found this, filled it in, tests passed, user verified E2E.
2. **Insidious drift**: Three separate query handlers (`GetWorkflowDefinitionsQueryHandler`, `GetWorkflowDefinitionQueryHandler`, `GetWorkflowDefinitionByIdQueryHandler`) all had identical tenant filter bugs — global templates (`TenantId = Guid.Empty`) were invisible to tenant-scoped users. One path got fixed during Phase-05 prior refactor; the other two got skipped. User hit it when asking "why can't I see CaseProcess in admin?" The real answer: it was there, but filtered out.

The frustrating part is we have `WorkflowDefinitionRepository.GetLatestByKeyAsync` (used by runtime) with correct fallback logic documented, yet 3 separate handler implementations drifted away from that pattern. No single source of truth for "how to query workflows across tenant scope" until now.

## Technical Details

**Phase 3 Implementation**:
- Added `ReturnCaseCommand` (CQRS) + `Case.RecordReturn()` method
- Endpoint: `POST /api/cases/{id}/return`
- FE: new `useReturnCase` hook, case detail component refactor
- 17 new tests, all passing
- Build: ✅ success

**Tenant Fallback Bug (Root Cause)**:
```csharp
// BROKEN (before fix)
var definitions = await context.WorkflowDefinitions
  .Where(x => x.TenantId == tenantId)  // ← filters out global templates
  .ToListAsync();

// FIXED (added fallback)
var definitions = await context.WorkflowDefinitions
  .Where(x => x.TenantId == tenantId || x.TenantId == Guid.Empty)
  .OrderByDescending(x => x.TenantId == Guid.Empty)  // tenant-specific first
  .ToListAsync();
```

Affected handlers:
- `GetWorkflowDefinitionsQueryHandler` (list all workflows)
- `GetWorkflowDefinitionQueryHandler` (get by ID)
- Both now follow `Repository.GetLatestByKeyAsync` semantics

**Test Coverage**: 4 regression tests added using `IReadDbConnection` mock + `ReceivedCalls()` reflection to verify fallback executed (can't easily mock EF in this context). Total workflow tests: 410/410 ✅

## What We Tried

1. **Initial confusion**: User saw `gov-case-processing` workflow in admin UI → assumed it was the case processing workflow → surprise when buttons didn't match. Root cause: E2E test seeder had orphaned demo data (`E2ETestDataSeeder.SeedGovCaseProcessingWorkflow`). Kept it (not deleted) but documented as orphan.

2. **Environment config**: `dotnet run` defaults to Production env → `VNeIdHttpConnector` throw. Solution: `ASPNETCORE_ENVIRONMENT=Development` explicit. Caught during local verification.

3. **Trust-but-verify saved time**: Phase 3 planner red-team flagged 3 factual errors in task spec (existing role perm, seeder transition already in place, role grant scoping). Prevented rework.

## Root Cause Analysis

**Phase 3 dead pipe**: No handoff protocol between workflow designer (defines transitions) and case handlers (implements them). Return was modeled but unimplemented. Resolution: explicit task to implement missing transitions.

**Tenant fallback drift**: Pattern established in `Repository.GetLatestByKeyAsync` (runtime path) never codified for query handlers. Each handler implemented independently, copy-pasted the strict filter, then one got fixed during refactoring. No cross-check. Impact: multi-tenant isolation bug — paid tenant can't see global templates.

## Lessons Learned

1. **Tenant fallback is a repeating pattern**: Added memory feedback file `feedback-workflow-definition-tenant-fallback.md` with checklist for all 3 query paths + future handlers. Prevents one-by-one drift.

2. **E2E test data pollutes admin UX**: `gov-case-processing` seeded in Development environment, visible in admin UI, but disconnected from actual Case workflow. Document all orphan test data + consider cleanup backlog.

3. **Environment var defaults bite hard**: `dotnet run` → Production (no dev config) → external connector throws. Local dev docs should call out `ASPNETCORE_ENVIRONMENT=Development` explicitly.

4. **Red-team before implementation saves days**: Phase 3 planner + code-reviewer caught 3 assumptions (perm exists, seeder state, role scope) before I touched code. Trust the review gate.

## Next Steps

1. **Immediate**: Push commits, update memory `project-state.md` to v2.127
2. **Phase 4** (enable flag globally + finalize): Depends on Phase 3 ✅
3. **Backlog**: Document E2E orphan workflows + consider cleanup + audit other test seeders for similar pollution
4. **Future pattern**: Tenant fallback checklist in memory — review before any WorkflowDefinition query handler added

## Commits & Files

**Commits**:
- `a657859a` — feat(cases,workflow): Phase-03 Return transition + admin tenant fallback hardening (28 files, 1270+/56-)
- `bb4a787f` — chore(cases): add workflowInstanceId field to CaseDto type definition (1 file, +1)

**Files modified** (selection):
- `src/modules/Cases/Cases.Application/Commands/ReturnCaseCommand.cs` (new)
- `src/modules/Cases/Cases.Domain/Aggregates/Case.cs` (+RecordReturn)
- `src/modules/Workflow/Workflow.Application/Queries/GetWorkflowDefinitionsQueryHandler.cs` (tenant fallback)
- `src/modules/Workflow/Workflow.Application/Queries/GetWorkflowDefinitionQueryHandler.cs` (tenant fallback)
- `src/web/src/hooks/cases/useReturnCase.ts` (new)
- `src/web/src/pages/CaseDetail.tsx` (refactor buttons)

**Test counts**:
- Workflow.Application.Tests: 410/410 ✅
- Cases.Application.Tests: 17 new (Return path)
- Total Phase 3: 27 new tests, all green

## Session Timeline

| Time | Event |
|------|-------|
| 11:45 | Phase 3 implementation start |
| 12:30 | Return endpoint + FE hook complete |
| 13:00 | User E2E verification → buttons work |
| 13:15 | User: "why can't I see CaseProcess in admin?" |
| 13:45 | Root cause: tenant fallback missing in query handlers |
| 14:15 | Fix applied + 4 regression tests added |
| 15:00 | Drift pattern documented + memory updated |
| 15:30 | Docs sync + session close |

---

**Unresolved questions**: None — phase complete, next action clear.
