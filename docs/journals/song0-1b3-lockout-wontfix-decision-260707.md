# Sóng-0 Phase-1 Slice 1B-3 — Decision to Keep AccountAutoDisabledIntegrationEvent Direct at-Most-Once

**Date**: 2026-07-07 16:45 Asia/Saigon
**Severity**: Medium
**Component**: LockoutEscalationService, outbox publisher criterion
**Status**: DECIDED

## What Happened

Slice 1B-3 was the final direct at-most-once integration-event publisher in Phase-1: `LockoutEscalationService.OnLockoutAsync:82` → `AccountAutoDisabledIntegrationEvent` via `messageBus.PublishAsync`. The original phase criterion was "0 direct at-most-once publishers" — all should route through the outbox for atomic durability. This row sat decision-blocked for 4 days on the assumption that converting it would require refactoring `UserManager.SetLockoutEndDateAsync` and `UpdateAsync` to be UoW-friendly (they are not — they own their own `SaveChanges` on Identity DbContext). Close-out scout discovered a second blocker (PII) that made the cost/value trade-off genuinely different from the other 3 conversions. User decided: WONTFIX-recorded, keep direct, ship doc-only fix.

## The Brutal Truth

The frustrating part is that the plan criterion ("0 direct") was treated as absolute without pressuring the actual cost on this one. We spent 4 days blocked on a refactoring that should never have been framed as mandatory — the event has a genuinely different risk/value profile (notification trigger, not audit record; best-effort by design; carries PII). The real kick in the teeth is that we only uncovered the PII blocker on close scout; it was invisible in the plan's one-liner "UserManager not UoW-friendly." We shipped 3 publishers through the outbox and hardened idempotency on the consumer side, so keeping this 1/4 direct is not a gap — it's a conscious exception with clear boundary.

## Technical Details

**Blocker B1 — No UoW seam:** `UserManager.SetLockoutEndDateAsync` and `UpdateAsync` call `await _dbContext.SaveChangesAsync()` directly and return to caller. There is no transaction context for `IOutboxWriter.AppendAsync` to co-write the outbox row atomically. Refactoring UserManager to accept an optional UoW would require:
- Adding a parameter to UserManager (breaking API for Identity module integrations)
- Unwinding SaveChanges logic into a transaction step (migration-tier risk)
- Auditing all internal UserManager call-sites to ensure they don't double-SaveChanges

**Blocker B2 — PII in event payload (NEW):** The event carries `UserName` + `Email` — personally identifiable information. Routing it through the durable outbox would persist PII at-rest in the `outbox.IntegrationEvents` table, violating the IDs-only invariant enforced by `EventSchemaDiscoveryTests`. The event also carries `AutoDisabledAtUtc` as `DateTime` (not `DateTimeOffset` — outside the guard's type allow-list).

**Why this is acceptable to keep direct:**
- The auto-disable security action persists independently on Identity.Lockout (durability achieved).
- Consumer (`AccountAutoDisabledConsumer`) is best-effort by design — it swallows email send failures without retry, treating notification as a courtesy.
- The event is not an audit record — the actual auto-disable is separately audited via the at-least-once audit publisher (on Slice 1B-2 path), so no audit gap under S2 extraction.
- Direct publish is transient (not persisted) — carrying PII is acceptable because it never hits the durable store.
- Materiality is LOW: lost event = missed notification; lost security action = separate concern (would have already failed at UserManager).

**Evidence:** `EventSchemaDiscoveryTests` correctly exempts `AccountAutoDisabledIntegrationEvent` from the IDS-ONLY rule because it does NOT implement `IExternalDomainEvent` — the outbox capture interceptor and PII guard never see it. The event's "NO PII" XML-doc comment was simply wrong (aspiration, not fact).

## What We Tried

**Path A: Refactor UserManager for UoW.**
- Cost: breaking API change + transaction plumbing + call-site audit across Identity integrations
- Value: atomicity on 1 event + satisfies absolute criterion
- Verdict: Rejected — cost exceeds value when combined with the PII blocker; if UserManager is not UoW-friendly by design, forcing it violates YAGNI

**Path B: Direct publish as-is, fix the docs.**
- Cost: one exception to the criterion; doc-only fix
- Value: preserves low-materiality at-most-once contract that is already acceptable by design
- Verdict: Chosen — criterion was absolute without pressure-testing edge cases; this site earns the exception

## Root Cause Analysis

The plan criterion ("0 direct at-most-once") was too absolute. It was pressure-tested across 3 sites (Forms, Audit, AccountDisable) uniformly — all "should be atomic." But the last site has a fundamentally different security/materiality profile: it's a _notification trigger_ (best-effort already), not a _durability-critical event_ (audit). The plan should have had a criteria rubric that distinguishes "notification at-most-once" (acceptable direct) from "audit/transactional at-least-once" (requires outbox). We treated "audit-like" as a synonym for "critical path" — it's not.

The secondary root is aspirational planning: the 4-day block on "UserManager not UoW-friendly" masked a deeper reason (PII) that only emerged during close-out scout. If we had red-teamed the event schema (IExternalDomainEvent check, PII carry, type signatures) during plan review, the PII blocker would have surfaced early.

## Lessons Learned

1. **Blanket conversion criteria need boundary pressure-testing.** A plan rule like "0 direct at-most-once" should have been hedged as "all producers EXCEPT low-materiality notifications." Absolute rules create technical debt when an exception site costs more than the rule saves.

2. **Distinguish notification at-most-once from audit at-least-once.** Not all events are equal. Durability-critical events (audit, form submission) are worth the outbox overhead. Notification events (email sent, UI update) that are already best-effort can stay direct if they carry no PII.

3. **Red-team event payloads early.** The PII issue should have been surfaced during schema review, not on close scout. "Does this event implement IExternalDomainEvent?" + "Does it carry PII?" should be checklist items for all new integration events.

4. **Aspirational comments hide real blockers.** "UserManager not UoW-friendly" was the stated reason for the 4-day hold. The real reason (PII + acceptable at-most-once contract) only appeared on deep dive. Planning comments should cite the actual constraint, not a symptom.

## Next Steps

1. **Doc-only fix (this week):**
   - Correct `AccountAutoDisabledIntegrationEvent` XML-doc from "NO PII" to explicit "TRANSIENT PII: carries Username + Email; not persisted; direct-publish only"
   - Add matching note at `LockoutEscalationService.OnLockoutAsync:82` explaining the exception: "Notification trigger, not audit-critical; at-most-once by design; PII carried transiently only"
   - Update plan doc (Phase-1 criterion) to state "3 out of 4 via outbox; 1 recorded exception: AccountAutoDisabled"

2. **Backlog:** If the event ever becomes audit-relevant during S2 extraction (audit-write saga redesign), revisit. Flag this decision-journal in that backlog row (re-audit under the new audit schema).

3. **Monitoring:** AccountAutoDisabledConsumer already logged; confirm no error spikes in staging that would indicate missed deliveries.

4. **Reference:** Add to `messaging-patterns.md` under "When Direct Publish is Acceptable": "(a) already at-most-once by consumer design, (b) carries PII transiently only, (c) not audit-critical, (d) materiality = notification, not durability."

---

**Status:** DECIDED  
**Summary:** Phase-1 closes with 3/4 publishers via outbox + 1 recorded exception (AccountAutoDisabled: PII blocker + low materiality justify direct). Doc-only fix (XML comment + decision note) + backlog flag for S2 audit revisit.  
**Journal:** `C:\AqtCoreFW\docs\journals\song0-1b3-lockout-wontfix-decision-260707.md`
