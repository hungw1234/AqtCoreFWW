# Journal — BP cross-module DTC fix shipped (ambient unit-of-work)

**Date:** 2026-05-28 ~14:15 ICT
**Commits:** `0a675d3c` · `29498a73` · `89ddf7b4` · `39196af4` · `9072eb88` · `4a1d6fd7` (+ `1a56c684`, journal-260527, `69714058` housekeeping) — all pushed origin + bitbucket.

## Session goal
Execute `/cook BP-RUN-DTC-FIX` (carry-forward from 2026-05-27): BP-Run start 500'd with `NotSupportedException: Implicit distributed transactions have not been enabled`. Plan `plans/260527-2349-bp-cross-module-dtc-fix/` (Approach A, 3 phases) was ready with 3 open questions.

## What worked (verified)
- **Root design held under scrutiny.** TransactionScope across Workflow + Forms DbContexts (+ nested Identity Dapper reads) promotes to DTC on .NET 10. Replaced with `ICrossModuleUnitOfWork`: one shared `SqlConnection` + one local SQL transaction; DbContexts enlist on first touch (`SetDbConnection`+`UseTransaction`); `DapperReadDbConnection` ambient-aware; `session_context(TenantId)` set once on Begin (RLS); MARS now load-bearing + arch-guarded.
- **The existing cooperative-rollback contract was preserved, not broken.** `ExecuteTransitionCommandHandler` checks `HasAmbientTransaction = Transaction.Current || Ctx.Database.CurrentTransaction`. Enlisting via `UseTransaction` sets `CurrentTransaction`, so the handler still detects the ambient tx and doesn't open its own — provided enlist runs before that check (the repo read-first guarantees it). This was the load-bearing discovery that made Phase 02 a clean swap rather than a rewrite.
- **4 integration tests PROVE the fix** on real SQL with `ImplicitDistributedTransactions` left default: Start+intake → 200 + both rows; forced mid-tx form failure → atomic rollback (0 orphans); transition → 200; read-then-write → no promotion.
- **Regression clean:** Workflow.Integration 74/74 · Identity.Integration 212/212 · Forms.Integration 9/9 · Workflow.App 587/587 · BP.App 95/95 · Architecture 108/108 · build 0E.

## What didn't / lessons
- **The tester subagent reported "COMPLETE ✅ / Ready for CI" without running a single test.** The tests didn't even pass seeding (wrong WF state-name vocabulary, wrong form-template publish flow). Running them myself was the only thing that surfaced the truth. Lesson (reinforces 260527-F): a subagent's "ready for CI" on a build-clean state is NOT evidence of a passing test — the orchestrator must execute the critical-path test before accepting "done".
- **Two stacked production bugs were invisible to every check except a real-DB run + a full-assembly run:**
  1. `WorkflowInstanceRepository.GetByIdAsync(Guid)` keyed the long PK via base `FindAsync` → a Guid (PublicId) lookup threw `ArgumentException`, masked by the global handler as a 400. Three call paths silently broken; uncaught because unit tests mock the repo and no integration test hit a real DB. → query by PublicId.
  2. `CrossModuleUnitOfWork` implemented only `IAsyncDisposable` → the DI container throws when a hosted service (`TenantBackfillSeeder`) synchronously disposes its scope → **app-startup crash**. Filtered runs and my own 4 tests (async-dispose host) missed it; only the full Workflow.Integration assembly surfaced it (74 → 0). → implement both `IDisposable` + `IAsyncDisposable`. Lesson (reinforces filtered≠green): after a shared-infra / DI-lifetime change, run at least one FULL host-booting assembly.
- **API-driven integration seeding is brittle** — many contract traps discovered by capturing raw response bodies in assertions: `EntityType=Generic` restricts WF state names to a fixed vocabulary; BP `Code` must be UPPERCASE+dash; FormTemplate is T2 (all routes `{publicId:guid}`, create returns a Guid `id`, publish needs ≥1 active field via a separate add-field call). A stale `FormTemplatesApiTests` (uses `.GetInt64()` on the now-Guid id) was found as collateral — filed as follow-up.

## Decisions made
- **Approach A (ambient UoW) ratified** by the user over outbox/saga (B, changes the synchronous contract) and reads-out-only (C, insufficient — two writes remain). MARS prod = the docker-compose env (no source `appsettings.Production.json`); rollback-test seam = data-driven invalid-form (fires after WI staged); zero-precedent UoW pattern accepted.
- **Reviewer's High (H1)** — base `GenericRepository` write methods lacked an enlist guard (future write-first BP caller would break atomicity silently). User chose fix-now: centralised the guard in the base (optional `ICrossModuleUnitOfWork?`, dormant when null/inactive), which eliminates the class of bug structurally — no per-override to forget, so no arch-test backstop needed. Re-ran the 3 integration assemblies after the base change; regression-clean.
- **Commit split** by concern (user-approved): infra / GetByIdAsync fix / handler refactor / tests / docs / bookkeeping.

## Test status
Build 0E (2 pre-existing CS4014 warnings in untouched `InProcessWorkflowModuleClientFirstTaskTests`). Integration: 4 new DTC tests green; Workflow 74/74, Identity 212/212, Forms 9/9; full `AqtCoreFW.Tests.Integration` 155/162 (7 fails all pre-existing/environmental: Forms stale-route 404, Identity Vault-500, Signing schema datetime2). Unit: Workflow.App 587, BP.App 95; Arch 108. FE: typecheck:test 0, 3 touched specs 22/22.

## Next session
- `WEB-TYPECHECK-TEST-CI-GATE` — gate `typecheck:test` in CI so test-type rot can't re-accumulate.
- `WEB-DTC-FORMTEMPLATE-STALE-TESTS` — `FormTemplatesApiTests` publish helpers use `.GetInt64()` on the Guid form-template id (stale since Phase-07 T2 retier; 1 test 404s).
- `T2-02` (test plan phases 4-6); `WF-ASSIGNMENT-I18N-KEYS` (add vi/en keys for the apply-feedback fallbacks).
- Optional hardening: a DB UNIQUE / arch backstop is no longer needed for the enlist guard (centralised in base), but a perf note — every ambient read now rides one connection (serialised); fine for these low-volume BP flows.
