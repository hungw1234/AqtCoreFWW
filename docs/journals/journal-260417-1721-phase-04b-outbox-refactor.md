# Phase-04b Cases→Notifications Outbox Refactor — Dual-Identity Bug Caught

**Date:** 2026-04-17 17:21 → 18:03
**Severity:** High (architectural, caught pre-merge)
**Component:** Cases domain, Notifications application, MassTransit integration
**Status:** Resolved (PR #33, commit `839fc17d`)

## What Happened

Phase-04 was shipping with a documented cross-module reference violation: `Notifications.Application → Cases.Domain` for in-process MediatR dispatch of a domain event. Phase-04b goal was to replace this with an integration-event pattern using MassTransit to publish a `CaseTransitionedIntegrationEvent`.

Scout correctly gathered:
- `OutboxInterceptor` removes external events from the aggregate's domain-events collection during SaveChanges (line 79-88)
- `CaseCreatedIntegrationEvent` was aspirational, never raised
- `DictionaryPublishedCacheInvalidationConsumer` was dead code, wired into `AddMessageBus` but never invoked from Program.cs

Scout recommended: dual-identity `CaseTransitionedEvent : IInternalDomainEvent, IExternalDomainEvent`.

**That recommendation was a regression trap.** I didn't catch it until advisor pushed back.

## The Brutal Truth

I almost shipped a bug that would have broken Phase-04's orphaned-events fix. The dual-identity pattern sounds clean in summary form but fails in execution: the `OutboxInterceptor` clears external events before `IDomainEventPublisher.PublishAsync(@case.DomainEvents)` fires, so the event never reaches the handler, re-orphaning the notification side effect.

Worse: I accepted the scout's synthesis without re-reading the OutboxInterceptor code myself. Four critical lines (79-88) would have caught it in 2 minutes. I trusted the summary instead.

Advisor's probe demand revealed a second hidden cost: `DictionaryPublishedCacheInvalidationConsumer` plumbing was completely broken (Program.cs didn't invoke the `registerConsumers` callback). I'd estimated this fix at ~15 min in an early summary; actual scope of Option B (full Phase-02c) was 1-2 days. Without the probe, I'd have committed to "5 hours is enough" and spent 8 hours on Phase-02c instead of 1h on Phase-04b.

Advisor's Option D (MassTransit direct, no Outbox) needed the same ~30-min Program.cs fix-up before it could work. Honest scope was 2.5-3h, not 2h.

## Technical Details

**Scout error:** Correctly identified the interceptor's behavior (`external events removed from domain-events collection`), then synthesized the wrong conclusion. No reasoning chain shown; just "do this." Pattern recognition failed on a detail that makes the difference between working and broken.

**My error:** Didn't spot-check the recommendation by reading 4 lines of code. Defender bias ("if scout found the facts, the synthesis is probably right").

**Code review finding H-1:** Docstring said "Failure logged but MUST NOT propagate" but the code had no try/catch. On RabbitMQ broker outage, a client would receive 500 even though the transition succeeded. Fixed: added `ILogger` param to `CaseTransitionHelpers.SaveWithConcurrencyGuardAsync`, wrapped `messageBus.PublishAsync` in try/catch, logged `LogWarning` for publish failures.

**Implementation scope (honest):**
- `CaseTransitionedIntegrationEvent` record, `CaseTransitionedNotificationConsumer` (30 min)
- Snapshot + publish helper + handler rewiring (45 min)
- Program.cs `registerConsumers` callback activation + test updates (30 min)
- H-1 fix: logging + error handling (20 min)
- **Total: ~2h actual + 30m review churn**

## What We Tried

**Option A (scout's dual-identity):** Would break Phase-04. Rejected after advisor's push.

**Option B (full Outbox phase-in):** 1-2 days, overkill for one event. Rejected after probe revealed the cost.

**Option D (MassTransit direct, no Outbox):** 2.5-3h. User chose this. Required Program.cs fix-up to activate dead consumer wiring.

## Root Cause Analysis

1. **Scout synthesis without reasoning chain:** Facts were correct; the jump from "interceptor removes external events" to "make it dual-identity" skipped the critical step: "dual-identity means it will be removed, so the handler won't fire." Probability that someone re-reads those 4 lines of code is high; probability they read the full reasoning is low.

2. **Scope estimation without proof-of-work:** Saying "MassTransit will work, ~2h" is cheap. Proving "this callback is actually invoked in Program.cs right now" is a 15-minute probe. The probe surfaced that the entire consumer activation path was dead, adding 30 min to the scope.

3. **Docstring-as-assertion rather than-assertion-as-code:** I wrote "failure must not propagate" but didn't implement it. The gap between intent and code was only caught by the reviewer. Lesson: write the docstring AFTER implementation, or better — treat docstring as specification that must compile and pass tests.

## Lessons Learned

1. **Trust-but-verify applies to scout agents.** Especially for architectural recommendations, re-read the critical lines yourself before committing. A 2-minute code check would have caught the dual-identity trap.

2. **Probe before committing to architecture.** When scope depends on "this integration will work," spend 15 minutes verifying the integration is actually wired today. Saves 3-5× time vs. full implementation fallback.

3. **Drive-by fixes should be surfaced, not hidden.** The `registerConsumers` callback activation changes runtime behavior (dormant → live consumer). Worth a line in PR title and dep-map to avoid confusion later when the consumer suddenly fires.

4. **Docstring → code drift is a real bug.** When they diverge, one is lying. Test against the docstring; reviewer will find the gap. Better: write docstring after code, or make it executable (the `LogWarning` IS the "log").

5. **Three phases in one day only works with narrow scope.** Phase-04 (1h5m) + Phase-05 (32m) + Phase-04b (~2h impl+review) = ~4h on-keyboard. Each phase was genuine, red-team cycles actually de-risked, advisor() earned its cost.

## Next Steps

1. **Phase-04b merged** ✓ PR #33 commit `839fc17d`.
2. **Phase-06 remains gated** on 14-day soak (Phase-05 merged 2026-04-17 17:20).
3. **Phase-02c backlog:** `CaseCreatedIntegrationEvent` needs same consumer pattern (L-2 from code review). Low priority; Phase-04b covers the architecture.
4. **Test infrastructure:** Consumer tests use mocked `ConsumeContext<T>` instead of MassTransit InMemoryTestHarness (M-3). Acceptable for scope; upgrade to harness post-Phase-06 soak if broker integration gets heavy.

**Status:** RESOLVED
