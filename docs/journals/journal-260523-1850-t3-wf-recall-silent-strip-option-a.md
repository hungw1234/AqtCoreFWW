# Journal — T3-WF-RECALL-ASSIGNMENT-Q closed Option A silent-strip (v2.137p105)

**Date:** 2026-05-23 ~18:30-18:50 ICT (~30min)
**Commit:** `031d0cc1` (impl) + `d1de95c7` (backlog flip) on `main`
**Backlog row closed:** T3-WF-RECALL-ASSIGNMENT-Q (P2 product-input, filed same day during plan 260523-1241 closure)

## Session goal

Resolve P2 product-input row from earlier today's plan closure: when a workflow transition has `IsRecall=true` AND FE sends a non-null `Assignment` payload, what should the BE do?

## What worked

- **Product gate FIRST**: presented 3 concrete options (A silent-strip / B explicit-reject / C document-only) + cycle estimates BEFORE writing any code. Per cook skill HARD-GATE, product-input-blocked rows cannot TDD until semantic ratified.
- **Option A ratified** — silent-strip server-side, FE unchanged. 1-LOC at handler call site: `Assignment = t.IsRecall ? null : MapAssignment(t.Assignment)`.
- **TDD red-green clean**: H_A7 unit + I_SaveAct_A6 integration both failed RED first → impl → both GREEN.
- **Anti-tautology guard in I_SaveAct_A6**: regression guard `nonRecallAssignmentCount==2` proves strip is SCOPED to recall (not blanket). Without it, a buggy `Assignment = null` (unconditional) would falsely pass the absence assertion.

## What didn't / lessons

- **Lesson reinforced (260522-E):** for "absence proves behaviour" tests, ALWAYS add a paired "presence proves scope" assertion. Cost = +5 LOC; benefit = false-green protection on future refactors.
- Reviewer flagged 2 carry-fwd Lows (FE comment cross-ref + grey-out Collapse for UX). Both deferred as non-blocking polish.
- Cycle time vs initial estimate: 30min actual vs 30min estimate. On-target.

## Decisions made

- **Strip at call site, not inside helper** (SRP). `MapAssignment(AssignmentInput?)` stays a pure DTO mapper; recall-strip business rule sits at the loop where `t.IsRecall` is in scope. Reviewer confirmed correct level.
- **No FE change this slice.** Server is authoritative; FE Collapse stays visible (user can configure but BE silently drops on save). UX grey-out is a separate FE-FU.
- **Direct-to-main push** per invariant-closure precedent ([[phr-v2-phase-06-closure-260522]]). Bundle of 3 commits (impl + backlog flip + this changelog/journal commit).

## Test status

- WF.App handler suite: 6/6 → **7/7** GREEN (+H_A7)
- Workflow.Integration scoped suite: 5/5 → **6/6** GREEN (+I_SaveAct_A6)
- Compile: 0E / 0W
- Code-review: **9.4/10 APPROVE** (0 Crit/H/M, 2 Low FE-UX carry)

## Next session

7 carry-fwd rows from plan 260523-1241 still open. Priority:

1. **T3-WF-EMPTY-CLAUSES-Q (P2 product-input, Phase 05 M2)** — semantically similar to T3-WF-RECALL: `Assignment != null && Clauses=[]`. Likely Option A (silent-strip to null) again if pattern holds, but needs product ratify.
2. **T3-WF-DEFAULT-CLAUSE-MODE-Q (P3 product UX, Phase 06c L5)** — current default Initiator OK?
3. **T3-WF-CONCURRENCY-FLAKE (P3 pre-existing)** — triage or quarantine.
4. **T3-FE-TESTS-DRIFT (P3)** — 9 unrelated FE test failures sweep.
5. **FE-FU grey-out Collapse on recall edges** — UX polish from this slice's carry-fwd.

## Pattern that emerged

The T3-WF-RECALL closure follows a repeatable shape that may apply to T3-WF-EMPTY-CLAUSES next session:

| Step | Recall-Q | Empty-Clauses-Q (likely) |
|---|---|---|
| Surface to user with 3 options (silent-strip / explicit-reject / status-quo) | ✓ | candidate |
| Ratify Option A silent-strip | ✓ | candidate |
| 1-LOC at handler call site | ✓ | likely (e.g. `MapAssignment` returns null when `Clauses.Count==0`) |
| 1 unit + 1 integration test with regression guard | ✓ | candidate |
| 30min cycle | ✓ | candidate |
| Direct-to-main per invariant-closure | ✓ | candidate |

If pattern holds, T3-WF-EMPTY-CLAUSES could ship in similar ~30min cycle next session.
