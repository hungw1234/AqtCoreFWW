# Journal — Audit Ingestion Swallow Fix (Cell #7 slice-2)

**Date:** 2026-07-15 · **Track:** Microservices completeness-matrix cell #7 "Audit-chain under distribution"
**Ships:** 1 (plan → design-first → review gate → implement → tester + code-reviewer → fix), commit pending — `MSVC-CELL7-INGESTION-SWALLOW-FIX`
**Plan:** aqt-next → kongming design-first → `/ak:cook`

---

## What & why

Same day, same cell as the tail-watermark slice (`journal-20260715-1130-*`), the other half of the gap: **L1**, the *silent ingestion swallow*. `AuditBehavior` writes the audit row synchronously after the business command already committed; if that write throws, the catch block ate it — no retry, no alert, business response still 200. Watermark closes tail-truncation (L2); this closes the loss-at-the-source (L1).

## Design-first paid off, twice

Kongming rejected two tempting shortcuts: rewriting to share the business transaction (no shared tx exists at `AuditBehavior` time — business command has already committed by the time audit fires), and a claim-first dedup ledger (wrong for a retry-durable consumer — `feedback-claim-first-dedup-only-for-at-most-once-consumers`, claim-then-fail on retry silently drops the event). Chose fallback-enqueue-on-failure: on write exception, enqueue an idempotent Hangfire `AuditIngestionRetryJob` keyed on MessageId; Hangfire's own Failed-state becomes the dead-letter.

The bigger find: kongming traced the request `CancellationToken` into the audit write path and flagged that a client disconnect *after* the business commit cancels the now-orphaned audit tx — swallowed by the same catch. That's plausibly the **dominant real-world L1 trigger**, not exotic SQL failures. Fix: extracted `AuditEntryPersister` and severed the token (`CancellationToken.None`) so a disconnect can no longer take the audit write down with it.

## The catch that actually mattered: C1 (prod-only false-green)

Code-reviewer flagged a CRITICAL finding I'd have shipped past: the retry job runs inside a Hangfire scope where `BackgroundJobTenantContext` has no ambient tenant. Nothing pins the row's tenant into SQL `SESSION_CONTEXT` before the retry INSERT. Under prod least-privilege (non-`db_owner`), RLS BLOCK rejects a named-tenant insert with no session tenant set → the retry itself fails silently → **audit still lost, just one layer deeper.**

Integration tests were green because they run as `sa`/`db_owner` (`IS_MEMBER('db_owner')` short-circuits RLS) — exact repeat of `feedback-sa-dbowner-false-greens-rls-assertions`. Fix: `AuditEntryPersister` opens its UoW with `BeginAsync(entry.TenantId)` (the existing tenant-override overload, same one anonymous public-form least-priv writes already prove out). Proven, not assumed: new non-`db_owner` test `AuditIngestionRecoveryLeastPrivilegeTests` (rls_test_app), with a non-vacuity guard showing the INSERT is BLOCKed without the explicit tenant pin.

## Course-corrections during tester/review

- Tester flagged `AuditOutboxAtomicityTests` (81/82) as a regression and proposed catching by exception type. Wrong fix — atomicity (row+outbox rollback together) was never broken; the *propagation* contract changed on purpose (swallow → durable retry). Updated the test to assert the new contract instead of reintroducing selective catch.
- M1: retry job switched from read-then-insert dedup to insert-then-catch-unique-violation — RLS FILTER blinds a read-first dedup check under least-privilege, so the read can't be trusted to detect an existing row anyway.
- Test-only artifact: two job runs sharing one `AuditDbContext` in-test threw an EF identity-map error, not the SQL AK violation the test meant to exercise. Fixed by giving the test a fresh scope per run, matching how Hangfire actually scopes each execution in production.

## Deferred (user scope decision)

**H1** — `AuditIngestionIntent` carries Confidential PII (Ip, UserName, DataSnapshot) as raw Hangfire job args. Violates the `PiiMaskingFilter` IDs-only standard; a poisoned/stuck job retains PII outside the audit boundary indefinitely. Filed as follow-up `MSVC-CELL7-INGESTION-PII-JOBARG` (T2, compliance-gated) rather than folded into this slice.

## Verification

Audit.Integration 82/82 · TenantIsolation 56/56 (incl. the new non-`db_owner` least-priv test) · Audit.Infrastructure unit 23/23 · SharedKernel `AuditBehavior` 10/10 · build **0W/0E**.

## Honesty — what this closes, what it doesn't

Closes L1 (headline ingestion-loss on synchronous write failure, including the disconnect-cancellation trigger) with a proven prod-viable retry path. Does **NOT** close: F-C crash-window (process dies between business commit and enqueue — no durable outbox for the audit write itself), F-G no durable store if Hangfire's own storage is unavailable, and audit-fail-open-vs-closed is still a business-response question, not an audit one. All three routed to FU-A auditor questions, not silently assumed solved.

## Lessons reinforced

- `feedback-red-team-before-implement` — design-first surfaced both the dominant real trigger (cancellation token) and a CRITICAL prod-only bug (C1) before a single line of retry-job code existed.
- `feedback-sa-dbowner-false-greens-rls-assertions` — reconfirmed a third time this arc: **any new audit background write needs a non-`db_owner` test**, no exceptions, sa-run integration tests will never catch a missing tenant pin.
- Moving an EF write off the request path and into a background job silently drops the ambient tenant/RLS context that request-scoped middleware used to set for free — must re-pin explicitly at the new write site, every time.

## Unresolved questions

- FU-A auditor: is fail-open (swallow-then-retry, current) or fail-closed (block business response until audit durably queued) the compliance-mandated posture for a synchronous-write failure?
- `MSVC-CELL7-INGESTION-PII-JOBARG` — timeline/owner for de-PII-ing the job payload; blocks nothing today but is a live compliance gap in Hangfire storage.

---

**Status: DONE** — L1 ingestion-swallow closed with tenant-pinned durable retry (kongming design-first caught the real trigger + a prod-only RLS false-green before ship); PII-in-job-args follow-up filed, not fixed.
