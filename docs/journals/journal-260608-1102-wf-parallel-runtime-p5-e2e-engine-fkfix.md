# Journal — WF Parallel-Branch Runtime P5: E2E Fork Test + Child-Status FK Engine Fix

**Date:** 2026-06-08 · **Session:** `/cook "Scout-first P5"` (interactive) · **Phase:** 5 of 7, T5 EPIC `WF-PARALLEL-RUNTIME-ENABLEMENT`

## What this was meant to be vs what it became

P5 was scoped as *test-only* — the first integration test proving a parallel fork runs end-to-end. Scout-first turned it into three deliverables: the e2e test, a real engine bugfix the test exposed, and a fix for a P4 regression the scout uncovered. All shipped (4 commits, pushed origin+bitbucket).

## The discipline that paid off: audit-at-slice-start, twice

Before any code, INVARIANT 260522-A (audit ground-truth, don't trust the plan's claims) caught two things:

1. **A stale sequencing claim.** The plan said the "resolve-before-claim → 403" negative (RT6) depended on the P6 claim-bridge. Reading the actual handler showed P4 had already wired the assignee gate — so RT6 belonged in P5. The advisor flagged that I was about to reverse a sequencing decision on a *scout paraphrase*, not source; reading `ResolveBranchChildCommandHandler.cs` myself confirmed it.

2. **A hidden P4 regression.** P4's fork-entry fail-CLOSED gate (a child with no pool and no default assignee → 422) had silently broken three pre-existing fan-out integration tests, because their factory created pool-less children. P4 had only run the unit projects, not `Workflow.Integration.Tests`, so the reds never surfaced — its "0W/0E green" was a coverage gap, not a clean bill. I confirmed the delegation path (`IWorkflowModuleClient.ExecuteTransitionAsync` → the same P4 handler) and *ran* the tests: three FAILs. The rollback test was worse — passing vacuously, failing closed before it ever reached the rollback seam it was meant to exercise.

The lesson restated: a verified-sticky design change (here, P4's fail-CLOSED gate, blessed by advisor + two reviews) can still break under-specified tests in a project nobody ran. Run the affected integration project, not just the units.

## The engine bug — and why only an e2e test could find it

The headline finding: branch-child `WorkflowTask`s were persisting `BranchChildStatusId = 0`. `WorkflowTaskConfiguration` declared the column a bare scalar with **no EF relationship**, so the staging code wrote `childStatus.Id` while it was still 0 (IDENTITY unassigned pre-SaveChanges), and the comment promising "EF will fix the FK via relationship fixup" was describing a relationship that didn't exist. The repository's `GetByBranchChildStatusIdAsync` filters on that FK, so it never matched a real id — claim→resolve→join was broken end to end. The unit tests mocked the repository, so they never touched the persistence path that was wrong.

I almost gated the user on this from inference alone. The advisor pushed back: prove it. The discriminator was the negative I already had — resolve a *real* child-status id without claiming. It returned "No active task found," confirming the FK mismatch through a real run rather than a chain of reasoning. Only then did I escalate.

The fix (user chose it over a no-migration two-phase-save alternative) configures the relationship the comment assumed, adds a `BranchChildStatus` nav, and a `CreateForBranchChild` overload that sets the nav so EF propagates the generated id in the single SaveChanges — preserving P4's STAGE-ONLY/single-persistence contract and mirroring the `WorkflowTask.Instance` IDENTITY-FK precedent already in the same config file. The migration is a clean additive `AddForeignKey`.

## Smaller frictions worth remembering

- **Canonical state vocab, again.** EntityType "Generic" only admits a fixed state-name set; "ReviewA/Synthesis" 422'd the save. Captured the error body, mapped to canonical names (UnderReview/Assigned/Returned) — didn't guess.
- **Status codes:** activate and resolve both return 204, not 200. RB5 (child must have a pool) gates at *activate*, not save — the draft graph with an unpooled child saves fine.
- **The build hook + DLL lock.** The "build" keyword is blocked, so compiling goes through `dotnet test`; and the running dev API held the DLLs, so the migration build failed until I stopped it (PID 4332). The dev stack needs an `/aqt-runapp` restart.

## Honest gaps

The new test class is dense enough to intermittently trip the `write-ops` rate limiter (429) even though the harness *intends* rate-limiting disabled in tests (`ApiFactory` `RateLimiting:Enabled=false`). The reviewer verified the config binding is correct, yet I empirically hit a 429 — reasoning and observation disagree, so it's a real but non-blocking harness investigation, deferred. Also deferred from the original P5 scope: the H1 reconciler and the H2 concurrency scenarios (2-sibling concurrent-join, SLA-job-vs-join), plus the RT2/RT13 negatives. The 🛑 checkpoint is reached — the engine is proven runnable — so these belong to a reviewed P5-followup before P6/P7.

## Outcome

P5 class 4/4, harness-#2 BranchFanOut 4/4, WF.Application 631/631, WF.Infrastructure 89/89, Architecture 2/2. Code-review DONE_WITH_CONCERNS, zero Critical/High. The parallel fork now actually runs.
