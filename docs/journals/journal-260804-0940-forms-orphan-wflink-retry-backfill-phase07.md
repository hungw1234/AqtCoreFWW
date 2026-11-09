# Journal — Forms orphan-workflow-link-retry Phase 07 (backfill + runbook)

**Date:** 2026-08-04 · **Branch:** main · Final phase — plan `260803-2219-forms-orphan-workflow-link-retry` COMPLETE.

## What shipped

Phase 07 = the ops backfill that seeds retry-armed rows for historical orphan submissions (from before
the retry job existed), so the Hangfire job drains them.

- **`scripts/backfill/forms-workflow-link-pending-backfill.sql`** — idempotent raw INSERT, explicit
  column list (never `SELECT *`), `NOT EXISTS` on the unique key `(SubmissionId, TenantId)`, arms
  `NextRetryAt=now`, tags `LastFailureCode='BACKFILL_ORPHAN'`. Orphan window ≥ 2026-05-21.
- **`docs/runbooks/forms-workflow-link-retry-backfill-runbook.md`** — VI runbook: dry-run / execute /
  verify / rollback / principal.
- **1 integration test** — reads the ON-DISK script (not a copy) and runs it against real SQL, seeds
  2 orphans (1 pre-covered), asserts exactly-1-armed + idempotent re-run.

## Scope correction (INVARIANT 260522-A)

The plan's §7.3 planned to write RT1-RT6 tests here, but scouting the shipped artifacts showed they were
**already shipped in Phase 04/05** (`FormWorkflowLinkRetryJobTests` 5 methods 1:1, `WorkflowLinkPendingApiTests`
RT6, entity mutator tests). Re-writing them would have been duplicate work. Phase 07's real remainder was
just the backfill + runbook + one new backfill test. Trusting the plan's task list verbatim would have
wasted a chunk of the phase.

## Ground-truth-first on the raw SQL (lessons #1 class)

The INSERT column list was built from migration `20260522144724` directly (not the plan's prose): 12
NOT-NULL columns incl. `ClassificationLevel=1` (enum Internal) and `CreatedBy=Guid.Empty`, `Id` omitted
(IDENTITY). The integration test runs the actual on-disk script, so any future column drift fails the
test — the exact mitigation for the `INSERT SELECT *` silent-failure class.

## Review fixes (DONE_WITH_CONCERNS → resolved)

Code-review verified column-shape 15/15, idempotency, predicate, transaction all correct — 0 critical/high.
Three doc-accuracy fixes applied:
1. **Runbook RLS guidance was factually wrong** — reviewer verified this table has NO DB RLS policy (only
   the EF query filter); the privileged principal is needed for the RLS-protected SOURCE `FormSubmissions`
   SELECT, not to avoid an INSERT block. Corrected script header + runbook.
2. **Rollback predicate over-broad** — a job-re-armed row is also `Pending` (`AttemptCount>0`); added
   `AND AttemptCount = 0` so rollback only deletes never-attempted rows.
3. **Cosmetic** — `NextRetryAt` now uses `SYSDATETIMEOFFSET()` (was implicit datetime2→datetimeoffset).

## Follow-up filed

`FORMS-WFLINK-PENDING-TABLE-NO-DB-RLS` (T3) — the tenant-scoped attempts table has no SQL RLS policy (only
app-layer filter); the 2026-05-22 add landed after the RLS-restore migration and no later migration adds a
predicate. Likely a gap; not a Phase-07 blocker. Needs a design call: deliberate app-filter-only or add RLS.
