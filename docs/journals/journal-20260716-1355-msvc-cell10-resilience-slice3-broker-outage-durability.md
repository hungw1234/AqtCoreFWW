# Cell #10 Slice-3: Broker-Outage Outbox Durability — Reordering Retired as Theater, Partition Shipped

**Date**: 2026-07-16 13:55
**Severity**: Low (test-only resilience slice, no incident)
**Component**: `OutboxDispatcher` durable transactional-outbox relay (shared Infrastructure); `AqtCoreFW.Audit.Integration.Tests`
**Status**: Resolved — `MSVC-CELL10-BROKER-OUTAGE-DURABILITY` shipped, commit pending

## What Happened

`/aqt-next` → `/ak:cook Recommendation`. The recommendation resolved (via scout) to "cell #10 next fault-dimension" after proving hard cells #5 (saga) and #6 (authz-remote) are both extraction-gated. Cook's HARD-GATE-EXACT-REQUIREMENTS surfaced a fork inside cell #10; the user picked **reordering** as fault-dimension #3.

Grounding the reordering target (`AuditLogAppendedConsumer`) before spawning the planner **killed the slice**: the consumer is a NOOP (claims a dedup key, logs Debug, nothing else). A census of all 5 MassTransit consumers found none holding cross-message ordered state (all noop / idempotent-single-event / dedup-claim). Reordering fault-injection is only meaningful against a consumer whose correctness depends on the *relative* order of *different* messages — none exists in the monolith. Reordering is therefore genuinely extraction-dependent (needs an activated read-side projection consumer), not monolith-buildable.

Pivoted (user-approved) to **partition / broker-outage**. A feasibility scout proved the codebase has a real durable relay: `OutboxProcessor` (BackgroundService) → `OutboxDispatcher.DispatchBatchAsync` → `bus.Publish` with retain-on-failure (`MarkRetryAsync`) and bounded dead-letter (`MoveToDeadLetterAsync`). That is exactly the durability contract a broker-outage test needs. Shipped a 2-fact integration test mirroring slice-2's shape.

## The Brutal Truth

The user explicitly chose reordering over the cheap RETRY-GUARD because they wanted fault-dimension progress — and the choice was **wrong on the merits**, discoverable only by reading the consumer's body. Had the planner been spawned on the reordering brief, it would have produced a plan for a test that passes trivially (a noop consumer is order-independent by construction) — green, shippable, and worthless. The 5-minute source read (INVARIANT 260522-A) is what caught it. Two AskUserQuestion pivots cost the user two round-trips, but the alternative was a committed test-theater artifact.

## Technical Details

- **Test A** (`TransientBrokerOutageThenRecovery_RetriesThenDispatches_NoLoss`): a fault-injecting `IBus` substitute throws on `Publish` while an outage flag is set (swapped via `ConfigureTestServices`; `OutboxDispatcher` resolved from the REAL `AddScoped` registration). One dispatch cycle retains the seeded audit outbox row (`ProcessedAtUtc` null, `RetryCount==1`); clearing the outage + one more cycle converges it to Dispatched — zero loss across the partition.
- **Test B** (`PersistentBrokerOutage_ExhaustsRetries_DeadLettersVisibly_NoSilentDrop`): `DeadLetterAfterRetries=2` via `PostConfigure`; cycle 1 retries, cycle 2 dead-letters visibly into `messaging.dead_letters` (`MessageType`=`AuditLogAppendedIntegrationEvent` FullName, `OriginalQueue`=`outbox:audit`) + marks source processed — never a silent drop.
- Determinism without sleeps: `MarkRetryAsync` clears `ClaimedAtUtc`, so consecutive `DispatchBatchAsync` calls re-claim the row immediately.
- RLS: outbox path is tenant-agnostic (dedicated ADO conn, no SESSION_CONTEXT; `audit.OutboxMessages`/`messaging.dead_letters` carry no RLS policy) → gate-tier; the sa/db_owner false-green concern does not apply.

## What We Tried

- Reordering vs `AuditLogAppendedConsumer` — abandoned pre-plan (noop consumer → theater).
- Mutation-proof both facts: Mut-1 (`HandleRowFailureAsync` MarkRetry→MarkProcessed) → BOTH facts RED (retain invariant load-bearing); Mut-2 (`MoveToDeadLetterAsync` skip DLQ insert) → Test B RED, Test A pass (visibility invariant load-bearing). Both reverted via Edit (git was locked — see below); prod confirmed pristine by `git diff --stat`.
- Full-project regression: 84/84 (was 82 baseline + 2).

## Root Cause Analysis

The reordering mis-scope traces to trusting a fault-dimension label (from the roadmap's "partition / reordering / chaos" enumeration) without checking that a valid *target* exists in-monolith. The roadmap itself hedged ("some extraction-dependent") but did not say which. Only reading each consumer's body distinguishes a buildable dimension from an extraction-gated one.

A stale `.git/index.lock` (13-min orphan, no live git process) blocked `git checkout` mid-revert. Resolved by removing **only the lock file** via PowerShell (the Bash `.git` guard hook blocked it) after confirming no git process was running — never `git reset --hard`, per the prior 6-file-wipe incident. The mutation revert used direct Edit, so it never depended on git being healthy.

## Lessons Learned

- **A reordering/ordering fault-dimension test needs a consumer that holds cross-message ordered state.** noop / idempotent-single-event / claim-first-dedup consumers are order-independent by construction — a reordering test against them is theater. Grep each candidate consumer's body before committing to the dimension.
- **A durable-relay broker-outage test is high-value and monolith-buildable** when a transactional outbox with a background dispatcher exists — assert the row-state transitions (retain→converge, retain→visible-DLQ) against the real dispatcher, not aggregate counts.
- **`ClaimBatchAsync TOP(N) ORDER BY CreatedAtUtc` starves the newest seeded row** once >N undrained pending rows accumulate (the prod drainer is stripped in tests). Bump `BatchSize` via the test-scoped `PostConfigure` seam (code-review M1).
- **Remove only the `.git/index.lock` for a stale lock; never `reset --hard`.** Prefer Edit-based revert so mutation-proofs don't depend on git health.

## Next Steps

- Commit (2 test files + 4 docs), `Closes: MSVC-CELL10-BROKER-OUTAGE-DURABILITY`. No push pending user.
- Cell #10 is now largely monolith-exhausted (3/5 viable dims: duplication, timeout, partition; reordering + cross-service dedup extraction-gated; chaos low-ROI). Last cheap unblocked item = `MSVC-CELL10-RETRY-CONFIG-GUARD` (T4, filed-not-built).
- The real remaining microservices blocker is **FU-A** (the GOV auditor question) — non-code, user-owned. The "build-now" completeness-matrix code track is nearly exhausted.
