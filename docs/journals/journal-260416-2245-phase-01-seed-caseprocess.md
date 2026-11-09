# Phase-01 Cases→Workflow — Seed CaseProcess Definition

**Date**: 2026-04-16 23:18
**Severity**: Medium (blocking later phases, not production)
**Component**: Workflow.Infrastructure, MasterData.Domain
**Status**: Completed

## Session Outcome

Phase-01 shipped: PR #26 OPEN and ready to merge. Spawned 3 commits on `feat/phase-01-seed-caseprocess-definition` off main (PR #25 merged 60e7d241). Phase-00 taxonomy baseline extended from 9→10 states; CaseProcessDefinitionSeeder (IHostedService) seeds global WorkflowDefinition with 7 states + 8 transitions + 6 permission codes. 23 new tests (16 unit + 7 integration), 134 MasterData regression clean. Code reviewer: 9.7/10 APPROVED (0 critical, 2 non-blocking medium).

## What Went Well

- **Option B decision held**: extending taxonomy with ReturnedForRevision instead of enum rename was the right call — zero breaking change for FE, idempotent seeder (INSERT-IF-NOT-EXISTS by DefinitionKey+Version) safe on existing DBs.
- **Auto mode worked smoothly**: 3 user gates via AskUserQuestion up front (branch strategy, naming drift, exec mode), no re-gates needed.
- **Subagent delegation (Steps 1-4)**: fullstack-developer turnaround was clean — implemented seeder, tests, registration in single shot. Only fix needed: IAsyncLifetime.InitializeAsync pattern for integration tests.
- **Finalize subagents chained fast**: tester (23/23 pass) → code-reviewer (9.7/10) → docs-manager (dependency-map + backlog updated) completed without friction.

## What Bit Us

**WebAppFixture.ConfigureTestServices strips ALL IHostedService** (line 68): Initial 7/7 integration tests FAILED with null WorkflowDefinition — seeder never ran. Root cause: test framework removes hosted services to skip Hangfire/MassTransit startup noise. Fix: test class implements IAsyncLifetime.InitializeAsync, manually resolves IServiceScopeFactory + ILogger from _fixture.Services, invokes seeder.StartAsync() before tests. After fix: 7/7 pass in 14s. Lesson: when seeder is IHostedService but tests strip it, manually call it. Captured as feedback memory `feedback-webappfixture-removes-hostedservices.md`.

**PermCode 3-part invariant almost tripped us**: naive plan-suggested `CASE.SUBMIT` (2-part) would fail `PermCodesTests.All_Should_Match_Module_Resource_Action_Format`. Fullstack-developer caught on first attempt, implemented correct form `CASES.WORKFLOW.SUBMIT` (3-part). Non-issue in final code, but reminder: invariant validation is strict.

**Test count updates required 3 iterations**: MasterData seeder baseline insert 10th state, tests expected HaveCount(9). Had to iterate across 3 test files (WorkflowTaxonomySeederTests, WorkflowTaxonomyCodesTests, PermCodesTests). Not a failure, just friction in step 0 before seeder work started.

## Technical Decisions

1. **Branching**: merged PR #25 to main, created fresh feat/phase-01-* branch off main (clean, no phase-00 partial state pollution).
2. **Naming drift**: chose Option B (extend dictionary with ReturnedForRevision constant + seeder row). Preserves FE parity, 0 breaking change, idempotent.
3. **Seeder pattern**: IHostedService registered in WorkflowInfrastructureModule. Uses DefinitionKey+Version existence check for idempotency (safe multi-deploy).
4. **Role↔permission bindings**: NOT hardcoded in seeder; deferred to runtime SyncRolePermissions API call. Seeder only defines codes, not assignments.
5. **Exec mode**: --auto (skip user gates on score ≥9.5). Reduced checkpoint overhead since 3 initial gates had high confidence.

## Deliverables

**Commits (3)**:
- 4bfb6cfb: masterdata-taxonomy — add ReturnedForRevision state (4 files, +31 LOC)
- b474e4a6: workflow seeder — CaseProcessDefinitionSeeder + tests (7 files, +629 LOC)
- bc35cb3d: docs sync — dependency-map + backlog (2 files, +22 LOC)

**Code**:
- CaseProcessDefinitionSeeder (209 LOC): 7 states, 8 transitions, idempotent
- WorkflowInfrastructureModule: registered as hosted service
- 6 CASES.WORKFLOW.* permission codes (SUBMIT, REVIEW, APPROVE, REJECT, RETURN, CLOSE)

**Tests** (+23, all pass):
- CaseProcessDefinitionSeederTests (16 unit, 206 LOC): state/transition cardinality, permission code presence, idempotency
- CaseProcessDefinitionSeederIntegrationTests (7 integration, 172 LOC): IAsyncLifetime pattern, full definition materialization check, DB existence assertions

**Regressions**: 134/134 MasterData unit tests pass (3 count updates 9→10 managed).

## Follow-ups (logged, not blocking)

- M1: misleading Activate() comment in seeder (code-reviewer note, low priority)
- M2: startup health check asserting CaseProcess v1 exists on host boot
- L4: DB unique constraint (DefinitionKey, Version, TenantId) before Phase-05 multi-replica scenarios
- Runbook: document re-run MasterData seeder in existing envs for ReturnedForRevision population

## Metrics

| Item | Value |
|------|-------|
| Branch | feat/phase-01-seed-caseprocess-definition |
| Commits | 3 (taxonomy ext, seeder+tests, docs) |
| LOC added | ~480 (209 seeder, 160 unit, 106 integration, +5 docs) |
| Tests added | +23 (16 unit, 7 integration) |
| Regressions | 0 (134/134 MasterData pass) |
| Code review | 9.7/10 APPROVED (0 critical, 2 medium non-blocking) |
| PR status | #26 OPEN, ready |
| Execution time | ~45 min (delegation + fix + review + finalize) |

## Next Phase Unblock

Phase-02 (extend IWorkflowClient contract) unblocks once PR #26 merges to main. Current dependency: CaseProcess v1 Definition + permission codes must exist before workflow commands can be wired in Cases module.

## Emotional Reality

Phase-01 shipped smooth despite the WebAppFixture gotcha. IHostedService removal in tests is a legitimate design (avoid noise), but docs/examples for seeder testing are sparse. Found it fast, fixed it fast, documented it. The option-B naming decision decision upfront paid off — zero rework. Auto mode kept us moving without interruption. Nice to end a phase with 0 regressions and a clean PR ready to merge.

**Confidence level**: HIGH. Code is production-ready pending code-review follow-ups (M1/M2 are non-blocking quality-of-life improvements, not correctness issues). Integration test pattern (IAsyncLifetime) is now reusable for future seeders.
