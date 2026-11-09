# Backup Restore-Drill Temp File Cleanup

**Date**: 2026-07-21 10:43
**Severity**: Low
**Component**: Backup automation — `DatabaseBackupService.ExecuteRestoreDrillAsync`
**Status**: Resolved

## What Happened

`ExecuteRestoreDrillAsync` runs a restore drill (restore a backup to a scratch DB, verify, drop it) but never cleaned up the 3 temp files it wrote to `Backup:Directory` — `_drill_*.bak`, `.mdf`, `.ldf`. Every drill run left the disk a little fuller. Added `DrillTempFileCleanup.TryDelete`, a swallow-and-log helper called from a `finally` block, deletes all three. Design call: the drill `.bak` itself is deleted too — it's an ephemeral restore-verify artifact, not a recovery source, so no reason to keep it around.

## The Brutal Truth

Small T4 row, but the backlog row's own description pointed at the wrong file. It claimed drill records were "excluded from retention prune" and cited `BackupRetentionPruner.SelectExpired` as the mechanism. Read the function — it's a pure selector with zero type filtering. The actual exclusion lives in the caller query at `DatabaseBackupService.cs:62-64` (`Type=="Backup"||"ScheduledBackup"`). The row's *claim* was correct, the *location* was wrong. If I'd trusted the symbol pointer without grepping the caller, I'd have gone hunting inside the pruner for logic that isn't there — classic time sink on a row rated "low effort."

## Technical Details

- New file: `DrillTempFileCleanup.TryDelete` — static, same namespace as `BackupRetentionPruner`, mirrors its unit-testable I/O-swallow pattern (catch, log, never throw).
- Called from `finally` in `ExecuteRestoreDrillAsync` for all 3 paths (`.bak`/`.mdf`/`.ldf`).
- Build: shared infra project, 0 warnings / 0 errors under `-warnaserror`.
- Tests: 4/4 new cleanup tests (including a locked-file swallow case), 6/6 existing pruner tests still green.
- Code review: DONE, 0 critical / 0 high.

## What We Tried

Nothing exotic — read the row, grepped `SelectExpired`, found no type filter, walked up to the caller, confirmed the real exclusion point, then wrote the cleanup helper matching the pruner's established shape (no new abstraction, just DRY reuse of an existing pattern).

## Root Cause Analysis

The leak existed because the original drill implementation focused on restore-and-verify correctness and never had a cleanup pass added — a straightforward oversight, not a design flaw. The backlog-row mislocation is the more interesting root cause: whoever wrote the row inferred the exclusion mechanism from the symbol name (`BackupRetentionPruner` "sounds like" the place that would know about drill records) instead of tracing the actual filter predicate.

## Lessons Learned

"Excluded from X" in a backlog row is a claim about *behavior*, not a pointer to *location*. Trace to where the filter predicate actually executes before touching code — a pure selector fn with no type param is a strong signal the row's symbol reference is wrong. Code review also flagged a real (non-regression) limit: if the drill fails mid-restore before `DROP DATABASE`, SQL Server still holds the `.mdf`/`.ldf` handles and `File.Delete` silently swallows the failure — leak persists on that one path. Pre-existing on every path before this fix, so accepted with an inline XML-doc note per the ops-hygiene threat model (no data loss, just disk hygiene) rather than over-engineering a retry/wait.

## Next Steps

None blocking — row closed. If drill-failure-mid-restore leak volume ever becomes a real ops pain, revisit with a retry-with-backoff on the locked-file case; not worth it now (YAGNI, no evidence of frequency).
