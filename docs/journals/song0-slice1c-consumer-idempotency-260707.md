# Sóng-0 Phase-1 Slice 1C — Consumer Idempotency via App-Level Dedup Ledger

**Date**: 2026-07-07 09:15 Asia/Saigon
**Severity**: High
**Component**: Outbox consumer dispatch, messaging infrastructure
**Status**: DONE

## What Happened

Outbox consumers in the custom OutboxDispatcher path can be delivered more than once because the dispatcher publishes via `bus.Publish` without pinning a stable transport MessageId. When a crash occurs between publish and mark-processed, the broker redelivery uses a fresh MessageId, and MassTransit's own EF inbox cannot dedup on that unstable ID. Slice 1C locked a solution: a shared app-level dedup ledger keyed on STABLE payload identifiers (FormSubmissionId, AuditEntryId, UserId:Ticks).

## The Brutal Truth

This is maddening because MassTransit already has an EF inbox feature, but it's useless for our custom dispatcher — we broke the contract that transport MessageId is stable. We built our own thing instead of respecting MassTransit's inbox semantics, so we own the dedup problem now. The frustrating part is that this is _exactly_ the kind of footgun that could have silently caused duplicate SMS notifications, duplicate account-disable security emails, or duplicate form acks in production if we shipped without this layer. We were one crash away from silent double-effects.

## Technical Details

**Chosen design: claim-first with UNIQUE constraint**
- `IProcessedMessageStore.TryClaimAsync(consumerName, dedupKey, ct) → bool`
- Atomicity via UNIQUE(ConsumerName, DedupKey) on `messaging.processed_messages`
- INSERT + catch SqlException 2601/2627 on duplicate → no TOCTOU
- MessagingDbContext, no RLS (cross-cutting ledger)
- Bounded: ProcessedMessageCleanupJob (hourly, >30d delete)

**Three consumers wired:**
1. `FormSubmissionAcknowledgedConsumer`: key = SubmissionId (Guid)
2. `AuditLogAppendedConsumer`: key = AuditEntryId (Guid); NOOP effect today
3. `AccountAutoDisabledConsumer`: key = `UserId:Ticks` (string composite); direct publish, no outbox

**Semantic choice (user sign-off):** claim-first for all three.
- Atomicity: no concurrent delivery can double-effect
- Trade-off: crash between claim and effect = lost effect (at-most-once)
- Rationale: FormSubmission ack low-materiality; AccountAutoDisabled already at-most-once by contract (direct publish)

**Code-review findings (DONE_WITH_CONCERNS, fixed):**
- (M2) Cleanup job's retention DELETE shipped untested behind false "verified by RunOnceAsync" comment; RunOnceAsync was internal with no InternalsVisibleTo → made RunOnceAsync public + added real integration test (seed 40-day-old + fresh, assert expired swept only)
- (L4) Stale Cases module references in touched files cleaned up

**Test matrix:**
- Unit 11/11 pass
- Integration: store 1/1, cleanup 1/1, Notif harness 3/3
- Arch tenant/RLS 6/6
- Full build 0/0 (green)

## What We Tried

Rejected: claim-after-effect. Would preserve at-least-once on crash but allows double-effect on concurrent delivery + introduces non-atomic check-then-mark TOCTOU. Not acceptable for security effects like AccountAutoDisabled.

Rejected: MassTransit's own EF inbox as-is. Keys on transport MessageId, which our custom dispatcher does not stabilize. Would require either (a) refactoring the dispatcher to pin a stable ID upstream (out of scope for 1C) or (b) accepting silent dedup failure (unacceptable).

## Root Cause Analysis

The root is architectural: we built a custom OutboxDispatcher that respects the outbox atomicity guarantee but does NOT preserve MassTransit's inbox invariant (stable transport MessageId across retries). This is a coupling mismatch — the dispatcher gains at-least-once delivery but loses the dedup foundation that MassTransit's inbox assumes. The fix is to implement dedup at the layer where we have the payload — keying on the business entity ID, not the transport ephemeral MessageId.

The secondary root (why cleanup-job test gap slipped through) is aspirational comments masking real gaps: "verified by RunOnceAsync" was true only if someone had written the test, which they hadn't. Code reviewer caught this pattern.

## Lessons Learned

1. **Custom dispatchers break inbox invariants.** When you replace MassTransit's dispatcher, you also replace its dedup foundation. Dedup must key on payload stable ID, not transport-level MessageId.

2. **Aspirational test comments are invisible tech debt.** Comments claiming "verified by X" without an actual test are a recurring failure mode. Reviewer red-team saved this one; it would have shipped untested.

3. **NSubstitute Task<bool> defaults to false.** Dedup substitutes in tests MUST explicitly `.ReturnsAsync(true)` or every consumer silently skips. This is not intuitive and burned early testability.

4. **Claim-first atomicity trades latency loss for concurrency safety.** Acceptable for low-materiality effects (form ack) and at-most-once events (account-disable). If you need at-least-once + idempotency, you need coordination — not the path here.

## Next Steps

1. **Backlog:** audit-consumer activation must backfill processed_messages for any claimed keys in the NOOP era (30-day window). Otherwise, audit events within that window will be silently skipped by the cleanup. (Deferred to backlog row for Phase-2.)

2. **Verify in staging:** run with real broker redelivery chaos to confirm no double-effects under E2E delivery failure scenarios.

3. **Monitoring:** add alerting for cleanup job duration (if >5s, possible lock contention on the ledger table). Add dashboard for deduplicated-event count (should be low; spike = possible consumer hang).

4. **Documentation:** update messaging-patterns.md to document claim-first dedup contract and when it applies (effects, not reads).

---

**Status:** DONE  
**Summary:** Implemented app-level claim-first dedup ledger keyed on stable payload IDs to prevent concurrent/crash-induced double-effects in outbox consumers; code review caught untested cleanup-job retention + stale references; all 21 tests pass; ready for staging chaos-test.  
**Journal:** `C:\AqtCoreFW\docs\journals\song0-slice1c-consumer-idempotency-260707.md`
