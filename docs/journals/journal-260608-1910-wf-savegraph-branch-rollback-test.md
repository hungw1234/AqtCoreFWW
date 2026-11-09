# Journal — Save-Graph Parallel-Branch Rollback Atomicity Test

**Date:** 2026-06-08 19:10 ICT · **Commits:** `4813a332` test(workflow) + `a86c097e` docs (pushed origin+bitbucket ahead=0) · **Mode:** `/cook` interactive, test-only

## Session goal

Close `WF-SAVEGRAPH-TXN-ROLLBACK-TEST` — fault-injection proof that `WorkflowDefinitionRepository.ReplaceGraphAsync`'s `TransactionScope` rolls back the parallel-BRANCH phase on a mid-phase failure. The 4 happy-path branch-persistence facts shipped with the authoring arc would pass whether or not the writes truly enlist in the ambient scope.

## What worked

- **Audit-at-slice-start (INVARIANT 260522-A).** Probed before opening: precedent `ThrowOnNthSaveInterceptor` + `P3_A4` exist (reusable); sibling `...BranchChildAssignmentTests` has 4 happy-path facts and **zero atomicity fact** → row real-ready (category d), claim accurate.
- **Read the impl, counted the saves.** `ReplaceGraphAsync` = 4 raw-SQL DELETEs then 4 SaveChanges (#1 states, #2 transitions, #3 transition-assignments, #4 branches) in one scope. Calls #2/#3 fire unconditionally even with empty specs → `ThrowOnNthSaveInterceptor(4)` reliably hits the branch phase. Branch children/assignments/resolutions persist within the single #4 save (aggregate nav) — no hidden 5th save.
- **2 facts, both green, atomicity HOLDS** (2/2; all `ReplaceGraph*` 10/10 together; build 0W/0E). No production hole — `TransactionScopeAsyncFlowOption.Enabled` enlists the raw SQL, the failed re-save does not wipe the def.
- Reused the existing interceptor (DRY, same namespace/assembly — `internal` accessible). Code-review DONE_WITH_CONCERNS 0C/0H.

## What didn't / lessons

- **Advisor caught a vacuous headline assertion — my own RT13 lesson recurring.** The interceptor throws in the `SavingChangesAsync` PRE-save hook, so on call #4 the branch rows never reach the DB → asserting `branches==0` in the fresh-insert fact (Fact A) is trivially true regardless of rollback. I had framed Fact A as the primary test. Reframed: **Fact B (re-save restore) is the load-bearing one** — it's the only test that exercises the leading raw-SQL `DELETE FROM WorkflowParallelBranches` enlisting + rolling back, because P3_A4's fresh seed deleted nothing. Reusable: *name assertions by what they DISCRIMINATE, not what you wish they proved* — a pre-save interceptor can't prove the throwing phase's own inserts roll back; only an EARLIER-committed row's survival can.
- **Hardened against a different false-green** (code-review Low-1): `.WithMessage("*ThrowOnNthSaveInterceptor*")` pins the throw to the interceptor — without it a name-drift `ResolveStateId` throw (also `InvalidOperationException`) before #4 would pass the test for the wrong reason.
- Treated this as an atomicity test, not a green-checkmark task (advisor): was prepared to STOP + HARD-GATE if Fact B failed (= real prod hole). It passed empirically — verified, not assumed.

## Decisions made

- 2 facts (user chose restore + fresh-insert over restore-only) — fresh-insert kept non-vacuous by including a transition-assignment so #3 writes a real row that must roll back.
- Every assertion scoped by `def.Id` (shared Testcontainers DB); seed on a non-interceptor context so `_callCount` starts clean at the re-save's #1; confirmed the reload path is query-only so #4 stays #4.

## Test status

New `WorkflowDefinitionRepositoryReplaceGraphBranchRollbackTests` 2/2 · `ReplaceGraph*` suite 10/10 together · build 0W/0E. No production code touched → no wider blast radius.

## Next session

- Clean stop. WF-parallel epic at 🛑 checkpoint before P6; the two remaining test-debt rows in that arc (this one + BP-run verify) now closed.
- Top carry-over: **P6 claim-bridge** (FE claim/resolve UI + RT7 assign-guard) — scout-first read-only, fresh-headed.
- Smaller open: `WFE-RATELIMIT-TEST-DISABLE` (~1-2h harness flake); BP fork-rollback fault-injection (the `ICrossModuleUnitOfWork` BP-run analogue of this test — different UoW machinery, separate row).
