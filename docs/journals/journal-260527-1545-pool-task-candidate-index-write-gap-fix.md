# Pool-Task CandidateIndex Silent-Data-Loss Fix (v2.137p121)

**Date**: 2026-05-27 15:45
**Severity**: Critical
**Component**: Workflow.Infrastructure — Pool Task Assignment
**Status**: Resolved
**Commit**: `db284dbb`

## What Happened

Pool-based workflow tasks (ByPermission/ByRole/ByGroup/ByOrgUnit assignment modes) were created with `CandidateRefsJson` populated but **zero `WorkflowTaskCandidateIndex` rows**. The inbox read-side queries the index table exclusively → every pool task was permanently invisible to users and unclaimable. A single-assignee task unaffected; pool tasks DOA.

Ship summary: v2.137p121, 16 files (10 modified + 6 added), 1 session start-to-ship. Review APPROVE-WITH-NITS 9.3/10. Full-assembly WF.Application 587/587, Architecture 107/107, WF.Integration 72/72 (+16 new regression tests).

## The Brutal Truth

This is **six slices of negligent testing** plus catastrophic architectural failure-mode blindness. All pool tasks created since May 21 (7 days, every slice touching assignment) were invisible to users. The read-side existed for 2 commits before the producer, and 6 RT2-15 slices extended the read-side with zero end-to-end test coverage. The backlog row asking "assert producer writes index" was architecturally **impossible** — the producer writes JSON, not index rows. The gap survived because every single test manually seeded the index, bypassing the real factory path entirely.

The data-loss was recoverable (JSON snapshot exists), so no user-facing corruption, but this is the kind of silent failure that emerges in production with 500+ invisible tasks and gets discovered 3 months later after 15% of work has silently piled up in dead-letter queues.

## Technical Details

**Gap identity:** `WorkflowTaskFactory.ApplyCandidateSnapshot` (line 82-86, `WorkflowTaskFactory.cs`) writes **only** `task.SetCandidate(json)` — materializes JSON snapshot, zero index rows. Read-side (`GetInboxAsync:219`, `IsPoolEligibleAsync:121`) queries `WorkflowTaskCandidateIndex` via EXISTS subquery, no JSON fallback. Index table always empty for production tasks.

**Git archaeology (E5 evidence chain):**
- `1b0a9263` (S10a 2026-05-20): "index rows defer to future migration slice"
- `ed36ffb0` (Phase 4-C 2026-05-21): pivots inbox to EXISTS-over-CandidateIndex **without shipping producer**
- `b4c0de2d` (factory extraction 2026-05-21): comment "no index rows this slice" carried forward
- `f600c5a1`, `5e405eb9`, `68287c52` (RT2-15 2026-05-22–26): 6 slices extend read-side, none add producer writes

Gap activated May 21; no monitoring caught it because all tests did this:

```csharp
// WorkflowTasksInboxTests.SeedCandidateIndexAsync — TEXTBOOK ANTI-TAUTOLOGY
WorkflowTaskCandidateIndex.Create(taskId: task.Id, ...)  // bypass factory
ctx.WorkflowTaskCandidateIndex.Add(row);                 // direct add
```

Result: unit + integration tests never exercised `factory → producer → index` path. Green tests proving nothing.

**Scope blast:** ALL four pool clause modes affected (ByPermission/ByRole/ByGroup/ByOrgUnit). Every operation:
- `GET /api/v1/workflow/tasks/inbox` — zero pool tasks returned
- `POST /api/v1/workflow/tasks/{id}/claim` — all claims rejected (404/not-eligible)

Affected tasks in DB: `WorkflowTask` rows where `AssigneeId IS NULL AND CandidateRefsJson IS NOT NULL AND Status IN (Open,InProgress)`.

## What We Tried

**Session arc (single orchestration):**
1. `/aqt-next` recommended RT2-15-PHASE-03-FU backlog row (P4 test follow-up)
2. **Audit-at-slice-start (INVARIANT 260522-A):** empirical probe → row premise ("assert producer writes index") is architecturally impossible
3. **Escalate to `/ck:debug`** (RCA detailed above) → CONFIRMED P1 silent-data-loss
4. **Filed T1 backlog row `WF-POOL-TASK-CANDIDATE-INDEX-WRITE-GAP`** with RCA+evidence
5. `/ck:plan` Option C (materialize in factory via nav-prop wiring)
6. **Red team (3 reviewers, 11 findings, 10 applied):** caught e2e test harness fiction, tenant boundary gap, casing parity unproven, soft-delete idempotency
7. `/ck:cook` Phases 01/02/03 (producer + regression test + backfill) → ship

## Root Cause Analysis

**Primary:** Index-write producer explicitly deferred in S10a commit message ("to a future migration slice that never landed"). This deferral was INTENTIONAL but the follow-up slice never materialized.

**Secondary (why gap survived 6 slices):** Anti-tautology pattern in all tests: manual `WorkflowTaskCandidateIndex.Create() + .Add()` bypassed the factory entirely. Tests were **structurally incapable** of catching the missing producer because the real factory-to-index path was never exercised. This is the monitoring-gap anti-pattern from INVARIANT 260525-C reinforced for the 7th time.

**Tertiary (why red team caught actual regressions):** E6 probe confirmed factory is called in production (all three task-creation handlers route through `StageFirstOrNextTask`). Regression risk was REAL, not theoretical.

## Lessons Learned

1. **Audit-at-slice-start catches architectural gaps, not just stale backlog rows.** The row asked to "assert producer materializes index rows" — empirically impossible because the producer writes a JSON snapshot. The premise contradicted the code. INVARIANT 260522-A applies to fresh same-day backlog rows when scope spans architectural boundaries (I/O paths, cross-module contracts, data-materialization order).

2. **The anti-tautology test pattern consumed an entire 7-day window without warning.** Every test manual-seeded the index, making producer→index invisible. This confirms INVARIANT 260525-C: when pattern-mirroring read-side extensions, MUST mirror the test-LAYER-counts of the original slice, not just production-layer counts. Missing 16 integration facts (cross-tenant isolation + ByRole/ByGroup/ByOrgUnit end-to-end) from June's plan across 4 slices.

3. **Red team re-validation of "verified-clean" claims paid off.** Reviewer hypothesized "ByPermission Trim-only, no ToUpper mismatch would mask pool tasks" — looked credible, but RT-4 verified: refs are canonicalized Trim+ToUpperInvariant at save-time by `WorkflowGraphValidator` (existing test `I_SaveAct_A3` proves it), and DB collation is CI_AS (case-insensitive). The counter-argument didn't bring new data. Verified-clean was verified-clean.

4. **Subagent delivery claims require empirical spot-check when they exceed scope.** Fullstack-dev was scoped Phase 01 only but delivered all 3 phases + claimed "prior session shipped code." Didn't trust the narrative — spot-checked: producer present (grep `CreateForStaging`), build 0W/0E, casing claim cross-checked against resolver source. All claims held. Cost: 5 min skepticism, prevented a false-green claim from propagating.

5. **EF nav-prop fixup (Option C) is the right invariant.** `WorkflowTask.Create(instance: instance, ...)` already propagates parent ID via nav-prop relationship in one SaveChanges (factory comment lines 58–59). Same mechanism applies parent→child. Preserves S7 single-SaveChanges invariant; avoids JSON round-trip; factory already has all predicate data. Cleaner than post-save backfill (Option A) or repository-layer plumbing (Option B).

## Carry-Forward / Residuals

**Closed** by this fix: producer materialized, regression test guards against reversion (manual-revert test fails loud if index rows disappear), backfill migration populates existing tasks, forward-only semantics prevent double-insert on migration re-runs.

**Low-severity residuals (noted, not blocking):**
- **L2:** ByPermission/ByGroup/ByOrgUnit missing integration end-to-end (unit-covered + collation-safe; integration test exercised ByRole). Optional follow-up.
- **L1:** No DB UNIQUE constraint on index (app-level NOT EXISTS + migration @start bound mitigate deploy↔backfill race; acceptable per red team).
- **Latent:** Branch-child pool path (ParallelBranchService uses direct `AssigneeId` today; Phase 02 adds fail-loud arch guard so future pool-on-branch path can't silently resurrect this gap). RT-10 backlog note.
- **Migration downside:** Backfill is forward-only (Down is no-op); requires `db_owner` login at apply time.

## Next Steps

- [x] Ship producer + regression guard + backfill (v2.137p121 `db284dbb` PUSHED)
- [ ] Backlog L2 follow-up: add ByPermission/ByGroup/ByOrgUnit integration end-to-end tests (cross-tenant isolation, real factory→index path)
- [ ] Backlog L3: deprecate manual `SeedCandidateIndexAsync` patterns in test helper (force real factory via `CreateWorkflowInstanceCommand` harness)
- [ ] Architecture guard: document ParallelBranchService bypass in code comment + fail-loud at runtime if pool assignment is attempted on branch task

**Ownership:** Closed this slice. L2/L3 deferred to backlog (non-critical; existing tasks now visible via backfill + forward prevention via producer fix).
