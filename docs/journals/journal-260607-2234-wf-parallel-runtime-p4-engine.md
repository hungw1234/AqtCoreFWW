# Journal — WF Parallel-Branch Runtime ENGINE (P4 of 7)

**Date:** 2026-06-07 · **Command:** `/cook P4` (interactive) · **Commit:** `da37f56f` (not pushed)

## Session goal
Execute Phase 4 of the parallel-branch runtime epic: the HIGH-risk hot-path engine that makes a fork actually run end-to-end (fork fires → child tasks pooled/claimable → atomic AND-join → continuation). Phases 1–3 (data-model, FE pool editor, branch-aware validator) shipped 2026-06-06; P4 was the gap that kept the runtime non-functional.

## What worked (verified)
- **Audit-at-slice-start paid for itself.** The P4 plan was written 2026-06-04, *before* P3 (06-06) changed the validator + repo. Scouting the live code first revealed P1/P3 had already closed RT11 (`GetBranchByStateIdAsync` eager-loads child assignment clauses) and already provided the fork-exit primitive (`GetInstanceBranchStatusAsync` filters InProgress) — shrinking P4's real scope. G2 was confirmed empirically (the WF_NO_HANDLER pre-check resolves the target's *outgoing* transitions; a fork-only branching state has none → 422 before the fork could stage).
- **Unit 1 (fork-entry + fan-out)** — branch-aware NOH gate with RT4 fail-CLOSED, RT1 fork-exit guard, candidate-index fan-out (RT8 pool-priority, RT12 Domain staging). Closes G2 + G4. Code-review verified all 8 adversarial points safe.
- **Unit 2 (atomic join)** — extracted a shared `IBranchJoinCompletionService`, routed all 3 callers through it, made join-completion atomic with a RowVersion winner-election. Code-review verified the winner-election is genuinely race-correct.
- Build 0W/0E throughout · WF.Application 631/631 · WF.Infrastructure 89/89 (incl real-SQL).

## What didn't / lessons
- **The plan under-scoped Unit 2.** Plan framed RT2 as "wrap join in a tx" on one handler. Scouting found **3 callers** (explicit resolve + complete-task + cancel-task auto-resolve), and the 2 auto-resolve callers both **swallowed the join failure AND lacked the continuation pre-check** — a silent-strand + advance-to-handler-less-continuation bug the plan never named. The honest fix was a shared extraction, not a one-handler patch. Lesson: for a "wrap X in a tx" task, grep every caller of X first — the blast radius is the real scope.
- **Naive bounded-retry would have introduced a double-history bug.** The advisor suggested "retry 1-2 in place." But `ApplyParallelJoinTransition` mutates the in-memory POCO (history + CurrentStateId); `ChangeTracker.Clear()` resets EF tracking but NOT the POCO, so retrying on the same instance double-adds history. Dropped retry entirely: per-request DI scope = separate DbContext + POCO, so a sibling race is cleanly resolved by the branch RowVersion at SaveChanges (loser → no-op), and RT1 blocks the only other instance writer. No retry needed. Lesson: "retry the operation" is unsafe when the operation has already mutated a shared in-memory aggregate.
- **Test-fixture artifact cost a debug cycle.** Two happy-path join tests failed after Unit 2 because the mocked `GetWithHistoryAsync` returns an instance whose gen-assigned `PublicId` differs from the command constant, so the new reload-by-`instance.PublicId` missed the stub (in prod they're identical). Fix: `Arg.Any<Guid>()` in the branch-status stub.
- **The fix moved one strand downstream, not away.** Code-review (H1) found a background SLA-job-vs-join race that can still strand on the explicit-resolve path. Validated against the real threat model: a fork-only branching state has no outgoing transition, so it can't carry an `AutoTransitionOnTimeoutId` → the SLA auto-timeout can't fire on it → the trigger is effectively unreachable for canonical defs, and the whole path isn't production-reachable yet (zero parallel defs). The durable fix is the reconciler, tracked for P5.

## Decisions made
- **Scope:** user chose the FULL engine (A+B+C+D) over stopping at a claimable-but-not-joinable half.
- **Contract (user, via AskUserQuestion):** on complete/cancel auto-resolve, a join-completion failure must NOT fail the user's task action — it succeeds, the failure is loud-logged for an async reconciler (eventual consistency over blocking a legit action for a backend config error).
- **Concurrency model (advisor + 2 reviews):** child-resolve commits separately (sibling visibility — else AND-join deadlock under READ COMMITTED); only join-completion is atomic; MarkResolved is the winner-election token inside that tx; no in-request retry.
- **Cadence:** committed P4 as one self-consistent engine increment (the engine is coherent: fork → claim → resolve → atomic join → continuation). Not pushed — left to the user.

## Test status
- WF.Application unit: **631/631** · WF.Infrastructure unit: **89/89** (Testcontainers SQL). Build **0W/0E**.
- **Coverage gap (honest):** the atomic/concurrency behavior (RowVersion winner-election, real tx rollback, continuation-task actually staged in one SaveChanges) is not meaningfully unit-testable with NSubstitute → deferred to the P5 integration e2e. Unit tests cover delegation + pre-check fail-CLOSED + happy-path-with-stubs.

## Next session
1. **P5 e2e fork test** — the gate that proves the engine: activate → Submit → fork fires → 2 child tasks pooled (candidate-index) → claim → resolve ×2 → AND-join → continuation task → Close. Closes the coverage gap.
2. Name the **two-sibling concurrent-join** + **background-job-vs-join** races as required P5 integration scenarios.
3. **Async reconciler** (`BranchTimeoutCheckerJob` extension) for the residual strand tail — blocking-before-prod.
4. Then 🛑 checkpoint → P6 claim bridge → P7 demo seed.
5. Decide push timing for `da37f56f`.
