# Journal — WF Parallel-Branch Runtime P5-FOLLOWUP: Concurrency Tests + Tracking-Reload Engine Fix

**Date:** 2026-06-08 · **Session:** `/cook "P5-FOLLOWUP H2 concurrency tests"` (interactive) · **Phase:** P5 extension (unplanned), T5 EPIC `WF-PARALLEL-RUNTIME-ENABLEMENT`

## Setup: scope was test-only, outcome was test + engine fix

P5-FOLLOWUP was meant to add the deferred concurrency tests (H2 scenarios) before the checkpoint close. The tests ran, but the second suite exposed a real production defect in the parallel-join logic that the synchronous path *designed* to handle but had been silently broken by a tracking-reload mistake. The fix was one line + full verification, user-approved via AskUserQuestion, kept after confirmation.

## The defect: concurrent resolve stalls the instance

**Scenario:** Two users in different sessions both assigned to parallel-approval siblings. Both resolve their child-status tasks concurrently (real HTTP, 10–20ms apart). Expected: both 204 + instance advances. Observed: both 204, instance stranded `InProgress`, all children resolved, and the AND-join never fires.

I ran the test and it failed 2 of 3 times (the third passed due to ordering luck). Deterministic reproduction via intentional 50ms sleep between the two resolves locked it down every time.

**Root cause:** `ParallelBranchService.cs:164` reloads the root instance after saving a child-status resolve, expecting to observe the sibling's committed state in the same transaction scope. The code used a tracked re-query:

```csharp
var instance = _dbContext.WorkflowInstances
    .Include(...)
    .FirstOrDefaultAsync(i => i.Id == instanceId);
```

EF Core's identity map returns the *same tracked entity instance* that was loaded before the save, unchanged, *without* fetching from the database. So when Sibling-B's resolve committed its child-status changes in a separate context, Sibling-A's reload never saw them — both resolvers read the AND-join condition as incomplete → neither completes it. The instance is left hanging.

The synchronous single-context path *intended* to handle this: reload-to-observe is a valid pattern when you have visibility into concurrent commits. But it requires `AsNoTracking()` to actually query the database.

**The fix:** Add `AsNoTracking()` to that reload. One line:

```csharp
var instance = _dbContext.WorkflowInstances
    .AsNoTracking()
    .Include(...)
    .FirstOrDefaultAsync(i => i.Id == instanceId);
```

After the fix, H2a (concurrent 2-sibling resolve) ran 3/3 clean, every time.

## The reusable lesson

EF Core's tracking identity map is a correctness feature for same-context determinism. But a reload that *intends* to observe external writes will silently return the stale tracked copy if you forget `AsNoTracking()`. The workaround (explicit `_dbContext.Entry(instance).Reload()`) exists because this footgun is common. Code that says "reload to observe concurrent commits" must either:

1. `AsNoTracking()` on the reload query, or
2. Explicitly detach + reload the entity.

Here, the comment already *said* the intent was to observe a sibling's changes, so the code was written *correctly in intention but wrong in implementation*. This is worse than a missing-feature bug — it's a latent race that only fires under specific timing and only e2e tests with real parallelism would find.

## Test structure and scope

`WorkflowParallelBranchConcurrencyTests` (3 facts):

1. **H2a: 2-sibling concurrent resolve (parallel approval)** — Two child-status tasks, both resolved concurrently via HTTP in separate sessions. Expect both 204 + instance to advance to post-fork state. Proved the tracking bug.

2. **H2b: SLA job fires after join completion** — Configure a delay-job scheduled for post-fork. Resolve siblings, verify the delay-job fires and is scheduled correctly (not re-executed). Scope: SLA should not race the join completion. Passes cleanly.

3. **H2c: Resolve after timeout returns 404** — A child-status task with a deadline in the past. Calling resolve returns 404 NotFound (the child has expired). Instance doesn't advance even though the expired child is marked done by the system. This is a negative — expect no-op. Passes cleanly.

Extracted shared `ParallelBranchTestSeed` helper from the original runtime suite to avoid duplication (definition, instance, fork setup). Both test classes now reuse the same scaffold.

## Verification and decision gate

Blast-radius: 956 tests passed (Workflow.Infrastructure 89, Application 631, Domain 145, Workflow.Integration 91 of 94 with 3 pre-existing skips, src-Integration 7). No new compilation errors. No code in the fix path had been changed since P4 merged, so the risk is only what was already wrong and now corrected.

Code-review: DONE_WITH_CONCERNS. Zero Critical/High. Two Low findings applied (naming consistency). The fix is internal to `ParallelBranchService` — no migration, no entity structure change, no contract change, no FE impact, no depmap change.

The scope gate: the original P5 plan called for test-only work here, but a production defect emerged. Per orchestration-protocol, I surfaced it to the user via AskUserQuestion with the empirical evidence (failure-reproduction, the code snippet, the fix, and the verification), and the user confirmed keep. This stays in P5-FOLLOWUP, not deferred.

## Smaller notes

- The fix is *synchronously correct* — ParallelBranchService doesn't use async background jobs for the join; the reload-and-check happens before returning 204 to the caller. So the single-SaveChanges model and the immediate cross-instance visibility both hold once AsNoTracking is added.

- H1 (the reconciler for stranded timeout-joins, which is asynchronous and a different failure mode) remains deferred. The reconciler handles a *different* root cause (a crash or network failure between "join-complete" and "apply-transition"), not the tracking bug. H1 and H2 are independent.

- RT2 and RT13 negatives (coverage gaps around branch-child pool validation and join-condition false-positives) also remain unbuilt, as originally planned.

## Outcome

3 new concurrency tests (H2a/H2b/H2c), all passing. 1 production engine defect fixed (tracking-reload race in join). Commits: `959e6753` fix(workflow), `8ff04108` test(workflow), `5c54127a` docs. Not pushed — branch ahead=3 (user commits separately). The 🛑 P5 checkpoint now includes proven concurrent-join correctness.

## Open items (P5-FOLLOWUP and forward)

- H1 reconciler (timeout-stranded joins, async background reconciliation) — unbuilt, carry forward.
- RT2 pool-member validation on false-positive branching join (edge case) — unbuilt, carry forward.
- RT13 (another edge case, TBD) — unbuilt, carry forward.
- RT4 runtime guard on incorrect branch-child state — carry forward to P6/P7.
