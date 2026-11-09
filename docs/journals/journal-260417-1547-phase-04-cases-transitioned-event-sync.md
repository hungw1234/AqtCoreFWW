# Phase-04: CaseTransitionedEvent Sync + Publisher Gap Fix

**Date**: 2026-04-17 15:47 → 16:53 (66 min)
**Severity**: High (event dispatch bug)
**Component**: Cases.Domain + Cases.Application (Transition handlers + event publishing)
**Status**: Resolved (PR #31 merged)

## What Happened

Started with straightforward Phase-04 scope: wire `CaseTransitionedEvent` to Case.Status sync via Cases handlers, estimated 2h. Red-team scout revealed 3 critical mismatches before implementation. Scope grew to 3h after discovering that `IDomainEventPublisher.PublishAsync` was never invoked in any transition handler — internal domain events were orphaned in the aggregate's event list, preserved by `OutboxInterceptor` but never dispatched to Notifications module.

## The Brutal Truth

This is frustrating because the bug was pre-existing and architectural. The pattern was established in Phase-03 (Collaboration, Identity, Signature, Rules modules all have working publisher dispatch), but Cases handlers were the outlier — and nobody caught it until 5 hours into Phase-04. If red-team hadn't reviewed the architecture before I started coding, I'd have shipped a handler that looks correct on paper but fires zero notifications in production.

The scope creep from 2h → 3h hurt schedule-wise, but fixing it NOW via a shared helper (`CaseTransitionHelpers.SaveWithConcurrencyGuardAsync`) is better than discovering the orphan in Week 3 when E2E tests reveal "no emails sent on case transition."

## Technical Details

**The orphan:** Five transition handlers (ApproveCaseCommandHandler, RejectCaseCommandHandler, etc.) and CreateCaseCommandHandler all called `SaveChangesAsync` but never touched `IDomainEventPublisher`. OutboxInterceptor preserved domain events in `Case.DomainEvents` list, but without explicit `PublishAsync` call, Notifications handlers never triggered.

**Root pattern check:**
```
Collaboration: ApproveCollaborationChangeCommandHandler → PublishAsync ✓
Identity: AuthorizeIdentityAccessCommandHandler → PublishAsync ✓
Cases: ApproveCaseCommandHandler → SaveChangesAsync → orphan ✗
```

**Fix vector:**
- Created `CaseTransitionHelpers.SaveWithConcurrencyGuardAsync(aggregate, publisher, context)` — single abstraction for all handlers
- Injected `IDomainEventPublisher` into 6 handlers (5 transitions + CreateCase)
- New handler: `CaseTransitionedNotificationHandler` — composes Subject/Body inline, fire-and-log pattern matching Rules module
- 8 new unit tests (6 theory + 2 facts) validating publisher dispatch
- 14 constructor updates in existing test fixtures

## What We Tried

1. **Plan-as-written approach**: Follow Phase-04 spec verbatim (implement `ToWorkflowStatus` mapping). **Result:** Red-team flagged architectural gap; would've shipped without discovering publisher orphan.

2. **Inline publisher dispatch per handler**: Each handler calls `PublishAsync` then `ClearDomainEvents`. **Result:** Rejected — 5× diff surface, 5× typo risk. DRY violation.

3. **Shared helper approach** (chosen): One abstraction handles concurrency guard + publisher dispatch. **Result:** Clean, tested, adopted by all 6 handlers in single edit.

## Root Cause Analysis

**Why the publisher gap existed:** Phase-03 was delivery-focused. Transition handlers were copy-pasted from Collaboration module pattern (which has publisher dispatch), but the publisher injection got dropped during Cases-specific refactor. Tests passed because they mocked everything; integration tests never ran (WebAppFixture gotcha with IHostedService removal — deferred to backlog).

**Why it wasn't caught earlier:** No code-review gate existed for Phase-03 handlers. Scope was large, review focused on happy-path logic not architectural completeness. This is on process, not individuals.

## Lessons Learned

1. **Red-team before code saves hours**: Scout revealed the publisher bug 30 min before first line of implementation. ROI on that review: ~6× (saved discovering this in integration tests or production).

2. **DRY decisions under pressure are worth the extra 15 min of abstraction design**: The shared helper added maybe 15 min design time but eliminated 5× repetition risk across 6 handlers. One future typo fix happens in one place, not five.

3. **Architectural exceptions must be explicit, not silent**: Code-reviewer flagged M-3: Notifications.Application refs Cases.Domain (cross-module). I documented it as explicit exception in dependency-map §8 with backlog ticket for Outbox refactor. This is honest. Hiding the exception would be worse.

4. **Plan drift is survivable when you catch it early**: Phase-04 plan was written pre-Phase-03. By session start, naming had drifted (`CaseTransitionedDomainEvent` → `CaseTransitionedEvent`). 30-minute plan sync prevented false starts.

## Next Steps

- **PR #31 merged** (20 files, 141+/42- lines). All 461 tests pass (32 Cases + 29 Notifications + 400 Workflow).
- **Backlog tickets created:**
  - M-2: Integration test coverage (WebAppFixture IHostedService workaround)
  - M-3 follow-up: Outbox refactor to promote event-publishing abstraction to SharedKernel.Contracts (~1-2h, scheduled post-Phase-04)
- **Next phase gate:** Phase-05 (Notification templates sync) can start immediately; no blockers.

---

**Session verdict:** Scope ballooned 50% due to architectural gap, but catching and fixing it in-session prevented a worse discovery later. Code quality 7.5/10 with explicit technical debt tracking.
