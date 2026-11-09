# Phase-04a B5: Entity Tiering + Event Renames (ai/collaboration/integration/notifications/reporting)

**Date**: 2026-05-03 13:56
**Severity**: High (architectural milestone + App/Infra breakage introduced)
**Component**: Domain layer entity refactor / foundation
**Status**: Resolved (B5 shipped; App/Infra deferred to Phase-4b)

## What Happened

Batch B5 completed phase-04a's entity tiering across 5 leaf modules: 22 entities (6 T2 + 14 T1 + 2 T3 plain-class) rebased from `Entity<Guid>` to `Entity` (long PK). Architecture.Tests project revived after B1/B2/B3/B4 — now green at 13/15 module allowlist. 8 IDomainEvent property/payload renames distributed across integration/collaboration/notifications. All 5 domain projects compile; App/Infra broken as expected (Phase-4b scope).

Commit: `77b8fc7f` (29 files, +376/-136).

## The Brutal Truth

**Good news**: Architecture.Tests is alive again. Design-guard rules can now run after B5b finishes (rules+search). Milestone unlocked.

**Bad news**: Three lurking cross-module design issues were exposed mid-refactor that WILL bite Phase-4b unless planned:

1. **Partner.Create event fires with `Id=0`** — Partner is T1 (no PublicId), event dispatch happens in domain factory BEFORE SaveChanges, so EF hasn't assigned the identity yet. PartnerCreatedEvent carries `partner.Id long = 0`. Handlers reading that event will log/process nonsense.

2. **MessageSentEvent.ConversationId had a type flip (Guid→long)** — This is an async event (sits in outbox queue). Subscribers holding stale serialized messages with `ConversationId Guid` will silently fail deserialization against new `ConversationInternalId long` schema. No error, just dropped events.

3. **MessageLog.Id/UpdatedAt don't exist in T3 plain-class** — Integration handlers reference `messageLog.Id` and `messageLog.UpdatedAt` expecting Entity base. Plain-class has only `long InternalId` (no UpdatedAt). Handlers will throw NullReferenceException at runtime.

## Technical Details

### Event Renames & Type Flips

| Event | Problem |
|-------|---------|
| ConversationCreatedEvent | `ConversationId Guid` → `ConversationPublicId` (T2 AK) ✓ logical |
| MessageSentEvent | `ConversationId Guid` → `ConversationInternalId long` — async queue issue |
| Partner events | `PartnerId Guid` → `PartnerInternalId long` yet Id=0 pre-save |

### Build Status

- **Domain projects**: 5/5 ✅ (Ai, Collaboration, Integration, Notifications, Reporting)
- **Architecture.Tests**: ✅ green (allowlist 13/15; rules+search deferred)
- **Application layer**: ❌ ~60 errors (handler FK type mismatch + MessageLog.Id missing)
- **Infrastructure layer**: ❌ expected (mapper refactor deferred)

### Plain-Class T3 Pattern Established

MessageLog + NotificationLog both follow OutboxMessage precedent (B2/Phase-2): `long InternalId` PK + `Guid MessageId` AK. BUT integration handlers written assuming Entity base (expect `.Id`, `.UpdatedAt`). Pattern conflicts with handler layer expectations.

## What We Tried

Followed phase plan strictly: rebase per-module, allowlist per-batch, defer handlers. Worked well for design validation — caught event type flips early. Didn't pre-flight handler impact because handlers are Phase-4b scope (correct decision). But cost is now Phase-4b has 3 blocking design decisions to resolve sequentially.

## Root Cause Analysis

1. **Partner.Id=0 timing**: Domain factory pattern (create + emit) assumes EF auto-assign happens immediately. It doesn't. Solution must be: save first, then emit. OR move event emission to application service. Phase-0 didn't anticipate T1 entities with PK-carrying events.

2. **Event type flip Guid→long**: Cross-module contracts (events) weren't pinned as immutable during Phase-1 planning. Outbox queue messages now carry stale schema. Queue drain + replay needed before Phase-4b deploy, or message format migration in subscription handler.

3. **MessageLog.Id vs InternalId**: Handlers were written when MessageLog was Entity<Guid>. T3 design (plain-class + IMessageIdentified) came later (Phase-03 deep-dive). Handlers never updated. Handler layer must adopt T3 contract: use `.InternalId`, don't expect `.UpdatedAt`.

## Lessons Learned

1. **Architecture.Tests as design-guard works.** Allowlist scoping lets you grow the test suite per-batch without waiting for the whole solution. Green flag: design violations surface within the SAME phase, not 3 weeks later in QA.

2. **T1 events carrying their own Pk require post-save dispatch.** Don't emit events in factory when entity PK is identity-assigned. Either refactor event dispatch to application layer (cleaner) or inject a persistence-aware event queue into domain factory (uglier, violates layer boundaries).

3. **Event contracts are cross-module client agreements.** Type-flipping payload fields (Guid→long) breaks subscribers. Need: (a) versioning strategy (event schema + subscriber version negotiation), or (b) immutable-once-published rule (migration = new event type). The Guid→long flip here is OK for internal messages (we control both sides), but the pattern teaches us: event types need governance.

4. **T3 plain-class means handler layer must adopt new idioms.** `.Id` doesn't exist → use `.InternalId`. `.UpdatedAt` doesn't exist → can't rely on it for optimistic concurrency. Handlers MUST be updated together with plain-class adoption, not deferred. Consider: rebase handler + tests in same batch-block (B5a domain + B5 handler together, not split).

5. **VectorDocument.SourceDocumentId Guid orphan question**: Files module not yet rebased. Points to FileRecord which will get `long Id` + `Guid PublicId` in B3. Need decision: keep Guid (reference PublicId, less efficient) or add `long SourceFileInternalId` (faster, leaks internal PK, breaks PII opacity)? Blocked on B3 Files module state.

## Next Steps

**Phase-4b must:**
1. Resolve Partner event timing: save-then-emit pattern in handlers + document contract
2. Drain outbox queue of old MessageSentEvent messages OR add schema-migration handler to parse Guid→long
3. Rebase all integration handlers to use `InternalId` instead of `.Id` and remove `.UpdatedAt` references
4. Decide VectorDocument cross-ref strategy before Files module Phase-4b

**Phase-4b finish criteria**: App/Infra compiles, integration tests pass, event queue replay validated.

---

## Unresolved Questions

- **MessageLog append-only mutators (MarkDelivered/MarkFailed/Acknowledge)**: Preserved per B3 precedent, but violates pure append-only semantics. Should T3 have a sub-tier "T3-mutable audit log" formalized?
- **VectorDocument.SourceDocumentId**: Guid orphan. Wait for Files B3 rebate + decide PublicId vs InternalId reference?
- **Partner event queueing pattern**: Post-save dispatch in handler? Or new post-save event hook in application service? Architectural style decision needed.

**Status**: DONE
**Summary**: B5 shipped — 22 entities + 8 event renames + Architecture.Tests revival. Domain 5/5 green, App/Infra broken expected Phase-4b. 3 critical design issues surfaced (Partner Id=0, MessageSentEvent type flip, MessageLog plain-class).
