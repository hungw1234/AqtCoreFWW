# Journal — WF Parallel-Branch Runtime Enablement P1→P3 (2026-06-06)

**Session:** 3× `/cook` (P1 resumed post-compaction, P2, P3) on the T5 EPIC
`plans/20260604-2205-parallel-branch-runtime-enablement/`. Appetite confirmed **FULL pooling**.
7 commits `4f133b56`→`4ad9e381`, all pushed origin+bitbucket ahead=0.

## Session goal
Move the parallel-branch runtime epic from authoring-only → toward runnable. Tonight: the first 3
of 7 phases — per-child assignment data-model + resolver (P1), FE pool editor (P2), branch-aware
validator (P3). Runtime fork fan-out (P4, HIGH) deliberately NOT started.

## What worked (verified + commit refs)
- **P1 data-model** (`4f133b56`/`35de55da`/`790d15be`): NEW dedicated `WorkflowBranchChildAssignment`
  + `...Clause` tables (1-to-1 nav, FK cascade), additive migration `20260606135304`. Resolver
  overload `ResolveAssignmentAsync` = behavior-preserving extraction of the clause loop. WF.Application
  621 / WF.Infrastructure 88 (+3 real-SQL) / Domain 145 / Integration 11.
- **P2 FE editor** (`14163e7f`/`f8cf8e76`): reused the controlled `TransitionAssignmentEditor` via a
  new optional `allowedModes` prop (transition path byte-unchanged). app typecheck 0E · vitest 29/29.
- **P3 validator** (`f242751a`/`4ad9e381`): Save+Activate R1 branch-aware (exempt branching +
  fork-only child-target, RT10 guard), RB5 pool mode-restriction + canonicalization. New NoTracking
  repo load. solution 0E/0W · WF.Application 631/631 · WF.Infrastructure 89/89 (incl RT9 real-SQL).

## What didn't / lessons
- **P3 CRIT-1 (the big one):** the first cut made ONLY `ValidateForActivateAsync` branch-aware and left
  `ValidateForSaveAsync` R1 rejecting the same parallel topologies. Net effect: a parallel def could
  never be *authored* through Save → the Activate branch-awareness was dead. Both the existing branch
  tests AND my new ones hid it because they asserted `Contain/NotContain(RB...)`, never `IsValid`.
  **Lesson: a branch-validation test MUST assert overall validity (IsValid / Violations.BeEmpty), not
  just the absence of one rule — else a completeness gap stays invisible.** Caught by code-review;
  fixed by making Save R1 symmetric (same name-keyed RT10 guard) + an `IsValid` regression lock + a
  Save-side RT10 negative lock.
- **RT9 trap:** a validator test that hand-builds the branch list can't catch an eager-load bug in the
  repo — the validator looks correct while production sees empty branches. Closed with a real-SQL
  integration fact (`GetParallelBranchesForValidation_EagerLoadsChildrenAssignmentAndClauses`).
- **CA1068:** `branches` param had to go BEFORE `ct` (ct stays last) — Release builds treat warnings as
  errors, and ct-not-last would trip CA1068 in CI even though Debug is lenient.

## Decisions made
- **OQ-A deviation (P1):** new dedicated tables over reusing `WorkflowTransitionAssignment`+discriminator
  — reuse would ALTER a seeded table (non-additive) + add a transition-hot-path regression surface; the
  OQ-A rationale rested on RT5 which is neutral (both options need the resolver overload). advisor +
  code-reviewer both confirmed.
- **Repo additive method over RT9 "expand tuple" (P3):** `GetParallelBranchesForValidationAsync`
  (NoTracking) instead of widening `GetForValidationAsync`'s tuple — lower blast radius (existing mocks
  untouched), and Activate only READS branches so NoTracking is correct (no canon writeback at Activate).
- **Mode restriction = ByPermission+ByRole (P2/P3):** not YAGNI but a correctness boundary — ByGroup/
  ByOrgUnit have no shipped claim/inbox read-side, so such a pool is unclaimable → AND-join deadlock.
- **Scope discipline:** declined to auto-start P4 (HIGH risk, hot-path, 4 engine bugs) late at night.

## Test status
solution build 0E/0W. WF.Application 631/631. WF.Infrastructure 89/89 (incl real-SQL Testcontainers).
FE (P2): typecheck 0E, vitest 29/29, eslint clean. Code-review: P1 DONE_WITH_CONCERNS→fixed; P2 APPROVE;
P3 two rounds (CRIT-1 found → fixed → DONE).

## Next session
- **P4 fork-entry (HIGH)** — now unblocked (resolver overload P1 + canonical-at-rest P3 + activatable
  P3 all in place): NOH-entry branch-aware (`ExecuteTransitionCommandHandler.cs:194` G2), fan-out writes
  candidate-index per child, + the 4 red-team engine bugs (RT1 fork-escape, RT2 join partial-commit
  stranding, RT3 no continuation task staged, RT4 partial-empty AND-deadlock). Hot-path — fresh start.
- Then P5 e2e (🛑 checkpoint), P6 claim bridge, P7 seed.
- LOW-1 cosmetic carry: branch-child canon error messages say "Transition '{child.Name}'" (reused
  helper; only on invalid JSON / unknown code) — defer.
