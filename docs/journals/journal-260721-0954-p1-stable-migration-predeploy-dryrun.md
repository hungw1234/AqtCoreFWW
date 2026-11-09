# Journal — 2026-07-21 09:54 · P1 migration predeploy dry-run + Hangfire boot-lock retry

**Row:** `P1-STABLE-MIGRATION-PREDEPLOY-DRYRUN` (launch-track P1, stable-monolith-first) · **Branch:** `feat/p1-stable-migration-predeploy-dryrun` · not yet committed (git-manager pending).

## Session goal
Close 2 independent P1 concerns from audit report §1 #6 + #3, both gated under "stable launch = #1" priority:
- **#6:** Hangfire recurring-job registration on API boot hard-fails on transient distributed-lock timeout.
- **#3:** no automated preflight guard against the known Forms dup-public-Code cross-tenant migration blocker recurring on a fresh env.

## What worked
- **#6 fix:** new `RecurringJobRegistrationRetry.cs` — classifies `DistributedLockTimeoutException` + `SqlException` (error 1222/-2) as transient, retries 3x at 2/4/6s backoff, fails soft (logs + continues boot) after exhaustion. Wraps the existing registration block in `Program.cs:~437-505` as a pure lambda — 12 call sites moved verbatim, zero behavior change to the calls themselves.
- **#3 fix:** new `scripts/260721-migration-preflight-check.sql`, read-only, mirrors the Forms guard pattern from migration `20260709082745` (dup public Code cross-tenant → `RAISERROR` sev-16 → non-zero exit). `OBJECT_ID` check guards first-deploy (table doesn't exist yet → pass, not error).
- Live-tested the SQL script against a real SQL 2022 container, 3 cases: table-missing → exit 0, clean data → exit 0, seeded-dirty data → exit 1. Not a fixture assertion — actually ran it three times against a real engine.
- Code-review (M1) sharpened the fail-soft rationale past my first-draft comment: the registrations are *upsert schedule definitions*, not job execution. Steady-state compliance jobs (audit purge QĐ742, RTBF SLA, alert-eval) keep running off the schedule already persisted in the Hangfire store regardless of whether this boot's re-register succeeds. Only a first-ever-deploy that exhausts all 3 retries is actually exposed, and that state is self-healing on the next boot. This is a materially stronger justification than "retry then fail soft" — rewrote the comment to say it.

## What didn't / lessons
- **Almost shipped a false pass on the preflight script.** First verification run was `sqlcmd -i script.sql | tail -20; echo $?` — `$?` captured `tail`'s exit code (always 0), not `sqlcmd`'s. Ran the dirty-data case, saw `0`, almost logged "preflight correctly detects dirty state and exits 0" — which is backwards and would have shipped a broken gate silently. Re-ran without the pipe (`sqlcmd -i script.sql; echo $?` directly) and got the real `1`. Filed `feedback-pipe-masks-real-exit-code` to memory — any exit-code check downstream of a pipe stage needs `PIPESTATUS`/no-pipe, not bare `$?`.
- **Scope got cut mid-row, on purpose.** The row as written bundled a third piece — wiring #3's script into a CI dry-run gate — but GitHub Actions is off by user choice (same constraint that's carried the coverage-gate row for weeks now). Confirmed with user: ship the two locally-verifiable pieces (boot-lock retry + preflight script as a manual/pre-deploy runbook step), leave the CI-gate wiring as a noted-not-filed follow-up rather than opening a new backlog row for something that can't be verified until Actions comes back on.

## Decisions made
- Fail-soft over fail-hard for #6: boot must not become dependent on distributed-lock contention succeeding within 3 tries — a hard fail here takes the whole API down for a Hangfire scheduling concern, not a functional one.
- Preflight script ships as a standalone runbook artifact, not a migration gate — no CI to hook it into right now, and hand-editing the migration pipeline to shell out to a `.sql` file felt like solving a problem (automated gating) the environment can't currently support (no Actions). YAGNI until Actions is back.

## Test status
Build: 0 warnings / 0 errors (`-warnaserror`). Unit tests: 8/8 (`RecurringJobRegistrationRetryTests`). Preflight script: 3/3 manual cases against live SQL 2022 container. Code-review: DONE_WITH_CONCERNS, 0 Critical / 0 High (comment-sharpening only).

## Next session
- CI-gate wiring for the preflight script is blocked on Actions being re-enabled — do not re-open as a fresh row, just carry it alongside the existing coverage-gate carry-forward.
- git-manager still needs to commit + push this slice (working tree currently dirty: `Program.cs`, new retry class + test, new SQL script, backlog/changelog docs).
