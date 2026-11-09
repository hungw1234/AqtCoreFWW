# Journal — 2026-06-19 21:45 — PendingTransactionCleanupJob: expiry-boundary coverage

**Slice:** `SIGN-P5-PADES-COMPLETION-INTEGRATION` (T2) · `/aqt-next` → `/cook` · test-only.

## The audit was the work

INVARIANT 260522-A audit-at-slice-start found the row **partial-stale** — and that finding shaped the whole slice:

- **Case (1)** (two concurrent sweeps → one Completed + one swallowed `DbUpdateConcurrencyException`) and **case (2)** (filter excludes async-callback rows) were **already shipped in the spike** via `PadesCompletionConcurrencyTests`. Implementing them again would have duplicated existing coverage — the trap the invariant exists to catch.
- **Case (3)** was the only genuine gap, but **misframed**. Reading the domain showed `PendingTransactionStatus` has just `Initiated/Completed/Failed/Expired` — no "CA-processing". Mysign `4003` is a *poll sub-code* (`MysignStatusMapper`, `IsTerminal:false`), never persisted, never changes the domain status; a 4003 row stays `Initiated`. The cleanup job expires purely on `Status == Initiated && ExpiresAt < UtcNow - grace`, so the real (and only) guard against killing an in-flight sign is its `ExpiresAt`/TTL — not any sub-status.

## What shipped

`PendingTransactionCleanupJobTests` (real-SQL Testcontainers) — 4 tests driving the **real** internal `RunOnceAsync` (reachable via `InternalsVisibleTo` already present on Identity.Infrastructure):

1. overdue Initiated → Expired (anti-vacuity guard)
2. future-TTL Initiated → stays Initiated (the row's real "not prematurely expired" invariant)
3. within-60′-grace → stays Initiated
4. Completed + past expiry → stays Completed (filter excludes non-Initiated)

Build 0E, 4/4 green (~28s). Test-only; 0 production change.

## Decisions / lessons

- **Drive the real job, don't mirror its filter.** `PadesCompletionConcurrencyTests` mirrors the poll-job query inline (`Where(...)` copied into the test). That risks the test passing while the job drifts. Here `RunOnceAsync` is `internal` and the integration assembly is in `InternalsVisibleTo`, so the test exercises the actual expire logic + grace-from-options + `MarkExpired` — no re-implementation.
- **A job with a doc-comment claiming test coverage had none.** `PendingTransactionCleanupJob`'s summary said "verified via direct DB manipulation in unit tests" — grep found zero. Doc-claims are not coverage; verify empirically.
- **Back-date through the EF change tracker.** The factory `Initiate(ttl)` only mints a future `ExpiresAt` (ttl must be positive). `db.Entry(tx).Property(t => t.ExpiresAt).CurrentValue = past` writes the private-setter property without raw SQL or table-name coupling.
- **Reframe, don't blindly implement.** The literal row asked for "4003 not prematurely Expired"; the faithful implementation is a TTL/grace-boundary test of the cleanup job. Surfaced the reframe to the user before coding (review-audit rule #3) rather than shipping a test against a non-existent status.

## Follow-ups

None. The residual case-1 nuance (proving the *job's* per-row catch swallows the concurrency exception, vs. the DB merely throwing) was offered and declined as low-value — the persistence guard is the real risk and is already covered; the per-row `try/catch` is a standard 3-line pattern shared with this cleanup loop.
