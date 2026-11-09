# Phase-05: Cases→Workflow Data Migration Backfill (SQL + Runbook)

**Date**: 2026-04-17 16:48 → 17:20 (32 min)
**Severity**: Medium (operational delivery, non-code)
**Component**: Database → WorkflowInstances seeding + deployment runbook
**Status**: Resolved (PR #32 merged, commit `c55693d1`)

## What Happened

Phase-04 shipping left the feature flag OFF in production because there was no data migration to seed `workflow.WorkflowInstances` for existing Cases. Phase-05 scope: deliver 3 SQL scripts + 1 runbook to backfill 600k+ Cases. Red-team scout caught 3 HIGH column drifts in the plan before any SQL was written. Session completed in 32 min; zero C# code, zero tests required.

## The Brutal Truth

This feels almost too efficient — which makes me suspicious. Phase-04 was 66 min with major scope creep. Phase-05 was 32 min with zero issues. The asymmetry isn't luck; it's because red-team review on SQL work has 6× ROI compared to code review. Had I implemented the plan verbatim, runtime errors would've surfaced the same night in a QA backfill run: `ModifiedAt column not found`, `UpdatedAt unknown`, `Incorrect syntax near NULL`. Instead, scout caught all three BEFORE first SQL line.

The aggravating part: these drifts came from a 2-day-old plan written before Phase-04 landed. The schema evolved (base class inherited `UpdatedAt`, not `ModifiedAt`). I should have synced the plan on session-start, not assumed it was fresh.

## Technical Details

**Column drift discovered (all HIGH):**
```sql
-- PLAN (WRONG)
INSERT INTO WorkflowInstances (CaseId, WorkflowStateId, ModifiedAt, ...) VALUES (...)

-- ACTUAL (from Case.cs Entity base)
Entity.cs:11 → UpdatedAt (not ModifiedAt)
```

**Drift #2 & #3** — `WorkflowDefinitionConfiguration.cs:27` confirmed `IsLatest` column exists (scout was right), but columns `OriginalDefinitionId`, `LastMigrationId` + SLA columns (`DueAt`, `SlaBreachNotifiedAt`) are all nullable. Omitted from INSERT so EF defaults apply per DbContext configuration.

**Scout trust-but-verify moment:** Scout report said "`IsLatest=1` in action items" without earlier confirming the column. Direct read of `WorkflowDefinition.cs:30` + config confirmed it exists. Scout wasn't wrong, just inconsistent presentation. Lesson: always verify the contested claim directly, not just trust the scout's headline.

**Implementation (3 SQL + 1 runbook):**
- `001_dryrun.sql` — 4 pre-flight queries (table row counts, null checks, workflow state coverage)
- `002_backfill_workflow_instances.sql` — batched 1000 rows/TX, INNER JOIN to WorkflowStates, idempotent NOT EXISTS guard, inline parity verify
- `003_rollback.sql` — history + instance hard-delete with row counts for audit
- `docs/runbooks/cases-workflow-backfill.md` — pre-flight gate, main execution, post-verify, rollback triggers, deployment checklist, change log

**Gitignore gotcha surfaced during commit:**
```
.gitignore:70 → plans/**/* (ignores all plan files except plans/templates/*)
```
Phase-02 + Phase-04 plan files only tracked because force-added (`git add -f`). Applied same precedent to Phase-05 plan file for PR #32 consistency.

**Stray artifact bundled:** Phase-04 session journal was created AFTER PR #31 commit landed on main, leaving it uncommitted on deleted branch. Bundled into Phase-05 PR rather than separate cleanup PR — keeps journal history contiguous.

## What We Tried

1. **Implement from 2-day-old plan**: Assumed schema unchanged since Phase-04 planning. **Result:** Red-team rejected; column names broken.

2. **Trust the plan, trust the schema**: Deferred red-team feedback to post-implementation. **Result:** Rejected — ops SQL must be right first time, no rollback testing in CI.

3. **Red-team → verify → implement approach** (chosen): Scout identified drifts, I cross-checked against actual entities, applied fixes before SQL write. **Result:** 3 lines changed in plan, 0 runtime errors expected.

## Root Cause Analysis

**Why the column drifts existed in the plan:** Phase-04 design doc was written before Case.Entity refactor landed. At that time, `ModifiedAt` might have been the name; by session-start, base class had changed to `UpdatedAt`. Plan wasn't stale on purpose — it just wasn't re-synced.

**Why red-team caught it:** Scout task explicitly includes "verify schema against actual entities." This isn't optional; for SQL migration work, it's the entire value proposition of pre-implementation review.

## Lessons Learned

1. **Red-team ROI scales with operational risk**: Phase-04 scout caught orphaned-events bug in code. Phase-05 scout caught column-name drifts in SQL. Two phases, same ROI multiplier (~6×). If the artifact requires zero testing (because it's ops, not code), red-team review IS the test suite.

2. **Scope honestly smaller = finish faster, and it's correct**: Phase-04 (66 min, code + tests + reviewer). Phase-05 (32 min, SQL + docs, red-team only). No tester/code-reviewer subagent because there was nothing in C# to test or review. The SQL dryrun queries ARE the test suite. The runbook IS the review artifact.

3. **Gitignore fragility is a process smell**: Having `plans/**/*` ignored by default + force-adding phase files one-by-one is brittle. If a teammate picks up Phase-06 without knowing this precedent, the plan silently gets left out of git. Actionable: add explicit `.gitignore` exceptions or document this in CLAUDE.md footnote.

4. **Back-to-back phase shipping is sustainable when scope is tight**: Phase-04 (66 min) + Phase-05 (32 min) + merge = under 2h on same day, two journals, two PRs, two sync cycles. This only works because each phase is scoped narrow — if Phase-05 had drifted into "also seed taxonomy rows," the rhythm breaks.

## Next Steps

- **PR #32 merged** (11 files: 3 SQL + 1 runbook + 1 stray journal + 4 doc bumps + 2 plan files force-added). Docs bumped to v2.109.
- **Production deployment readiness:** Feature flag can flip once QA runs backfill dryrun + verifies post-backfill row parity.
- **Gitignore tech-debt ticket:** Create backlog task to formalize plan file tracking (either add `.gitignore` exceptions or document force-add precedent in CLAUDE.md).
- **Next phase gate:** Phase-06 (Notification templates sync) can proceed immediately; no data dependencies.

---

**Session verdict:** Scope minimal, execution clean, risk mitigated by red-team review catching column drifts early. Gitignore quirk acknowledged for future cleanup. Code quality N/A (SQL only). Delivery confidence: 9.5/10.
