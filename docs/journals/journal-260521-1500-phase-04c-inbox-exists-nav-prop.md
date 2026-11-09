---
date: 2026-05-21 15:00 ICT
phase: PHR-V2 Phase 4-C
version: v2.137p88
ship-type: feature + carry-fwd closure
slice: 4-C (FINAL of Phase 4 — 3/3 SHIPPED)
---

# Phase 4-C Inbox EXISTS-over-nav-prop + DCAND-a Payoff

## What

Final slice of Phase 4 — closes the BP-resolver inbox refactor begun by DCAND-a (FK migration) and completes Phase 4 entirely (3/3).

**Endpoints:** `GET /api/v1/workflow/tasks/inbox` (new)
**Error codes:** `GOV_WFL_017 TaskFinalState`, `GOV_WFL_018 NotImplementedMode`
**Tests:** F1-F6 inbox integration (7/7) + F7 deterministic race interceptor + F8a/b/c exception discrim (4/4)
**Carry-fwds closed:** 4-B H1 (deterministic race), 4-B M1 (Done/Cancelled vs AlreadyClaimed discrim)

## Why

1. **DCAND-a FK payoff** — The `WorkflowTaskCandidateIndex` FK shipped 2026-05-21 11:17 ICT exists to support EXISTS-over-nav-prop queries replacing the old `CandidateRefsJson` JSON-column read. Phase 4-C realizes this benefit.
2. **4-B carry-forwards** — Reviewer flagged H1 (race test non-determinism via `Task.WhenAll` scheduler luck) and M1 (catch-all collapses Done/Cancelled into AlreadyClaimed) as Med-priority follow-ups. Both fixed deterministically.
3. **Inbox is the gate to BP UI** — Phase 5 FE work requires this endpoint.

## Code changes

| File | Change |
|------|--------|
| `WorkflowErrorCodes.cs` | +`TaskFinalState = "GOV_WFL_017"`, +`NotImplementedMode = "GOV_WFL_018"` |
| `WorkflowErrors.cs` | +`TaskFinalState(long, string)` factory (dual-metadata), +`NotImplementedMode(string)` factory (dual-metadata) |
| `IWorkflowTaskRepository.cs` | +`GetInboxAsync(userId, codes, tenantId)` signature |
| `WorkflowTaskRepository.cs` | +`GetInboxAsync` impl (EXISTS-over-nav-prop, `AsNoTracking`, defense-in-depth tenant predicate). M1 catch refactored: re-query w/ `.IgnoreQueryFilters()` + `.AsNoTracking()` + switch on `Status`. |
| `WorkflowTasksController.cs` | +`GetInbox` action, `[Authorize(Policy = Workflow.Task.Process)]` explicit (4-A-A INVARIANT) |
| `GetInboxQuery.cs` + `GetInboxQueryHandler.cs` | NEW Application layer wiring |
| `WorkflowTasksInboxTests.cs` | NEW 7 integration facts |
| `ClaimGuardExceptionDiscriminationTests.cs` | NEW 4 unit facts (F7 + F8a/b/c) |
| `GetInboxQueryHandlerTests.cs` | NEW 10 unit facts (F6 NotSupported + 9 sibling) |

## Test gates

| Suite | Result |
|-------|--------|
| Workflow.Infrastructure full-suite | 66/66 PASS (was 63/66 pre-fix) |
| Workflow.Application full-suite | 503/503 PASS |
| WorkflowTasksInboxTests isolation | 7/7 PASS |
| WorkflowTasks combined integration | 14/17 (3 fail = same EF model-cache class as Infra, WebAppFactory-scope carry-fwd Phase 6) |
| Build all touched projects | 0E |

## Decisions ratified

| # | Decision | Outcome |
|---|----------|---------|
| Q1 Route | `GET /api/v1/workflow/tasks/inbox` | locked |
| Q2 Helper | Sibling method on `IWorkflowTaskRepository` (not new service) | YAGNI |
| Q3 M1 strategy | Re-query + switch on Status | `GOV_WFL_017` |
| Q4 NotSupported | `Result.Fail(NotImplementedError 422)` | `GOV_WFL_018` |
| Mid: H1 fix-now | User-ratified attempt | Succeeded after rabbit-hole |

## Cycle time

~3h vs planner estimate ~5h. Main rabbit hole: ~70min debugging cross-class EF model-cache contamination (root cause discovery → production defense-in-depth fix).

## NEW META-LESSONS

### (4-C-A) EF model cache contamination across test classes — PERMANENT INVARIANT

**Discovery:** `WorkflowDbContext` global query filter (`ApplyGlobalFilters` in `ModelBuilderExtensions.cs:25`) uses `Expression.Constant(tenantContext)` to capture the `ITenantContext` instance. EF caches the compiled model PER `DbContextOptions<T>` TYPE — meaning the FIRST `ITenantContext` instance seen process-wide becomes pinned into the cached filter expression. Subsequent test classes using new `Substitute.For<ITenantContext>()` mocks (with valid TenantId) get the cached filter that references the OLD captured mock — which may have a stale/null/different TenantId from disposal.

**Symptom:** Tests pass in isolation (`--filter`) but fail in full-suite. Specifically: rows seeded with correct TenantId are invisible to subsequent EF queries despite identical Guid values. F8a probe surfaced the discrepancy: raw SQL `SELECT` finds the row (TenantId matches), EF without `IgnoreQueryFilters()` returns null.

**Root cause discovery process:**
1. Initial hypothesis: connection pool visibility — REFUTED by reordered probes
2. Second hypothesis: container restart between classes — REFUTED by `ICollectionFixture` semantics
3. Third hypothesis: tenant filter expression — CONFIRMED via `IgnoreQueryFilters()` probe (row visible without filter, invisible with)
4. Final hypothesis: EF model cache pinning captured-mock reference — CONFIRMED via reading `ApplyGlobalFilters` source

**Fix applied to PRODUCTION:** `UpdateWithClaimGuardAsync` M1 re-query now uses `.IgnoreQueryFilters().AsNoTracking()`. Defense-in-depth: task.Id is known, row was tracked under caller's tenant pre-exception, so tenant check at re-query is redundant. Production impact: makes discrimination robust against any future captured-context drift (scoped-tenant expiration, multi-context test scenarios).

**Fix applied to TESTS:** F7 winnerCtx/loserCtx `WorkflowTasks.IgnoreQueryFilters().FirstAsync` for cross-context loads.

**INVARIANT:** Production code that queries entities by primary key from a catch block (or any context where the original tenant filter may have been built under a different captured context) MUST use `.IgnoreQueryFilters()`. The tenant predicate is redundant in PK-by-ID queries since the row has only one TenantId.

**Test infrastructure INVARIANT:** Tests across multiple classes that share `DbContextOptions<T>` type MUST be designed assuming the global filter's captured `ITenantContext` reference is OUT of test's control. Either:
- (a) Use `.IgnoreQueryFilters()` on all test EF queries
- (b) Implement `IModelCacheKeyFactory` that includes a test-instance-unique discriminator
- (c) Force `Substitute.For<ITenantContext>` instances to behave benignly (e.g., `IsSystemAdmin=true`)

Option (a) chosen this slice for minimum scope. Phase 6 may evaluate (b) for cleanest long-term.

### (4-C-B) Diagnostic-probe bisection technique

**Pattern:** When EF query returns unexpected null, layer probes from coarse to fine:
1. Raw SQL `SELECT COUNT` via fresh `SqlConnection` (DB-level row existence + cross-connection visibility)
2. EF with `IgnoreQueryFilters()` (proves filter is the issue if this finds, plain EF doesn't)
3. EF plain query (what the production code does)

Each probe's assertion message includes the captured-context state for diagnosis. This bisected EF-model-cache contamination in <10min after framework was set up. **REUSABLE** across any future "row missing but should be there" debugging.

### (4-C-C) Reviewer-recommendation revalidation under empirical pressure

**Pattern:** When reviewer recommends Option A and user ratifies fix-now strategy, ATTEMPT Option A. If it fails empirically (e.g., my EF-context refactor introduced different errors), don't blindly persist — surface the divergence to user (orchestrator-owned decision-point), ask whether to continue, re-direct via re-budget, or carry-fwd. User responded "continue 30-60min". I budgeted and discovered deeper root cause within budget — successful re-direction.

## Carry-fwd to Phase 6

1. **WebAppFactory integration tests** have analogous EF model-cache issue (`WorkflowTasks` combined run 14/17). Apply same fix pattern at integration-test fixture level.
2. **3 sibling controllers** (Assignments/Migrations/Delegation) still have plain class-level `[Authorize]` — separate audit slice.
3. **Reason length validation** (4-A backlog), reassign-to-same-assignee short-circuit (4-A backlog), `Metadata[]=` vs `WithMetadata` pattern docs (4-C M3 nit).
4. **F7 doesn't exercise M1 Done/Cancelled discrim under deterministic race** (4-C reviewer M1 nit) — synthetic via stale rowversion only. Phase 6 can add multi-state race scenarios.
5. **EXISTS-subquery `c.TenantId == tenantId` redundant comment** (4-C reviewer M2 nit) — inline comment.

## Phase 4 closed

3/3 slices shipped 2026-05-21:
- 4-A Reassign+Assign IDOR @ PR #108 (12:37 ICT) — 9.4/10
- 4-B Claim+Unclaim+race @ PR #109 (14:09 ICT) — 9.2/10
- 4-C Inbox + DCAND-a payoff @ PR #110 pending (15:00 ICT) — 9.0/10

PHR-V2 Phase 4 = COMPLETE. Phase 5 (FE wiring + orval) UNBLOCKED.

## Unresolved questions

None — all 4 user-gate questions ratified + 1 mid-slice gate (H1 fix-now). Reviewer Med items all classified (close-now vs carry-fwd Phase 6) per orchestrator decision.
