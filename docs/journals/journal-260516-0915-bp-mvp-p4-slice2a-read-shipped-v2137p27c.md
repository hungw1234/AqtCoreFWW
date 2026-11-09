# Journal — BP MVP P4 Slice 2A-Read shipped (v2.137p27c)

**Session:** 2026-05-16 08:18 → 09:15 ICT (~55 min)
**Branch:** main · **Pushed:** origin/main `27107b14` + `2e21ec5e`
**Ship counter:** #1 of 2026-05-16 (clean morning, ship-#1-of-day band respected)
**Carry-in:** untracked P4 Slice 1 journal from prior session

## Flow

1. `/aqt-next` snapshot — fresh morning, last marathon ended ~00:05, 1 untracked journal carry, branch in sync. Recommended Option C → A hybrid: housekeeping commit + planner sub-slice P4 Slice 2 → ship first sub-slice.
2. Housekeeping commit `27107b14` — close P4 Slice 1 journal (single `docs(journal):` commit, no source change).
3. Spawned planner subagent → 2A/2B/2C sub-slicing report (`planner-260516-0820-bp-p4-slice2-subslicing.md`). 2A = Definition CRUD + auth policies, ~10-12h.
4. Advisor gut-check on 10-12h scope vs memory's "ship-#1-of-day band" rule. Advisor confirmed the cut is vertical-atomic (endpoint+policy+handler must ship together) NOT horizontal-atomic (read and write can split). Recommended further split → **2A-Read** (~3-4h) + 2A-Write (~6-8h). Took it.
5. Scout sweep ~10 min: Identity ModuleRegistration auto-registers 1 policy per `PermCodes.All` — **no Program.cs touch needed**. ApiControllerBase has ResolveTenantId + ToApiResponse. Rules module is closest pattern reference (Dapper IReadDbConnection + Pattern-A handler guard).
6. Cooked 2A-Read directly: 2 DTOs file + 4 query files + controller + Application.Tests project (csproj + 2 test classes). 15 files +506 LOC. One compile error mid-run (`PaginationMeta.PageNumber` vs `.Page`) — fixed inline. Final build 0E/0W.
7. Tester subagent: 5/5 + 19/19 + 2/2 + 97/97 + 10/10 PASS across BP.Application + BP.Domain + BP.Infrastructure + Architecture + PermCodesTests suites.
8. Code-reviewer subagent: **9.4/10 APPROVE_WITH_NITS**, zero critical/high. L-1 (LIKE-escape `_` `[` un-escaped), L-3 (ListActive cap doc), L-6 (SQL emission integration test) filed as Slice 2A-Read-FU LOW.
9. project-manager + docs-manager spawned in parallel (no file overlap) — synced plan.md / phase-04 / backlog and changelog / roadmap / dep-map.
10. Single `feat(businessprocess):` commit (`2e21ec5e`) pushed to origin/main. 15 files +506/-7.

## Decisions

- **D1 (scope cut Read+Write):** Memory's "ship-#1 band" rule overrode planner's 1.5d 2A. Horizontal split (Read first, Write later) is clean because cache + invalidation belong together in Write — Read needs no cache.
- **D2 (defer cache to 2A-Write):** YAGNI. Cache without invalidation is dead optimization for a read-only ship. IMemoryCache plumbing + Microsoft.Extensions.Caching.Memory pkg deferred — ships with mutations.
- **D3 (defer GetIntakeFormSchema):** That endpoint needs a Forms cross-module client that doesn't exist yet. Cross-schema raw SQL banned by arch test (P2 lesson). Defer to 2B where IBusinessProcessQueryClient + Forms client wire-up happens together.
- **D4 (no cross-schema JOIN for WorkflowDefinition PublicId):** DTOs surface `WorkflowDefinitionId` as bigint internal FK. PublicId resolution via IWorkflowModuleClient defers to 2C runtime — atomic with the runtime endpoints that need it.
- **D5 (no new auth policy registration site):** Identity.Application.ModuleRegistration already loops `PermCodes.All` → policies auto-registered. Zero Program.cs touch.

## Lessons

- **Lesson 1 (vertical vs horizontal atomicity):** Planner's "endpoint without handler = 500" argument is correct **vertically** but doesn't preclude horizontal Read/Write splits. Read-only API is a complete vertical (controller + handler + policy all ship). When ship-band discipline conflicts with planner's atomic-block, prefer horizontal split if no half-shipped feature breaks.
- **Lesson 2 (advisor catches scope-discipline blind spots planner misses):** Planner optimized for code-organization atomicity but didn't weight the memory rule. Advisor (full context) saw the gap immediately and recommended split. Pattern: advisor first when memory has a hard rule the planner may not surface.
- **Lesson 3 (`PaginationMeta.Page` not `.PageNumber`):** Forms/Rules paged DTOs use `Page` (record positional param matching SharedKernel.Application.Pagination.PaginationMeta). My controller route param is `pageNumber` (REST convention) — these are intentionally different names. Tests must use `.Page` not `.PageNumber`.
- **Lesson 4 (scout-discovered policy auto-registration):** Plan step 2 says "register 3 policies in Program.cs" — but the Identity loop auto-handles every PermCodes constant. Step is no-op for any module with PermCodes-based auth. Save ~10 LOC + Program.cs edit churn. Codify: when adding new PermCodes, the policy is auto-registered by Identity.

## Carry-over

- **2A-Write** (~0.5-0.8d, P1 next): 4 command handlers (Create/Update/Activate/Deactivate) + Pattern A + audit + outbox for Activate/Deactivate + IMemoryCache wrap + cache invalidation on mutations. Controller POST/PUT endpoints (#4/#5/#6/#7). Tests ≥6.
- **2B** (~0.3-0.5d): `IBusinessProcessQueryClient` (5 methods) + InProcess impl. Unblocks P6/P7/P8. Includes `GetIntakeFormSchema` endpoint via Forms cross-module client (or as part of Workflow contract).
- **2C** (~0.8-1d): Runtime endpoints (Start Run / Transition / Submit State Form) + ambient TransactionScope + integration tests.
- **Slice 2A-Read FU (LOW):** L-1 LIKE-escape `_` and `[` (cross-module sweep), L-3 doc ListActive 100-cap, L-6 SQL emission integration test.
- **P3-FU1** (~3-4h) still open: full BP TenantIsolationTests fixture-based enforcement.

## Tool incidents

- **PowerShell `$null` redirect ambiguity:** Bash tool default shell on this Windows session uses `/usr/bin/bash` not PowerShell. `$null` redirect failed twice; switched to PowerShell tool for native `2>$null`.
- **gitignored plan dir + tracked plan.md:** Same trap as prior session, mitigated correctly this time — used `git add -u plans/260420-.../plan.md` for the tracked-modified path while normal `git add` for new untracked source files.

## Files (15)

**Source (6 new):**
- `src/modules/businessprocess/AqtCoreFW.BusinessProcess.Application/DTOs/BusinessProcessDtos.cs`
- `src/modules/businessprocess/AqtCoreFW.BusinessProcess.Application/Queries/ListBusinessProcesses/{Query,Handler}.cs`
- `src/modules/businessprocess/AqtCoreFW.BusinessProcess.Application/Queries/GetBusinessProcessById/{Query,Handler}.cs`
- `src/modules/businessprocess/AqtCoreFW.BusinessProcess.Presentation/Controllers/BusinessProcessesController.cs`

**Tests (3 new):**
- `tests/unit/AqtCoreFW.BusinessProcess.Application.Tests/AqtCoreFW.BusinessProcess.Application.Tests.csproj`
- `tests/unit/AqtCoreFW.BusinessProcess.Application.Tests/Queries/ListBusinessProcessesQueryHandlerTests.cs` (3 facts)
- `tests/unit/AqtCoreFW.BusinessProcess.Application.Tests/Queries/GetBusinessProcessByIdQueryHandlerTests.cs` (2 facts)

**Modified (6):**
- `src/AqtCoreFW.slnx` (+1 line, Application.Tests entry)
- `backlogs/backlog.md`, `docs/project-changelog.md`, `docs/development-roadmap.md`, `docs/dependency-map.md`, `plans/260420-.../plan.md`
