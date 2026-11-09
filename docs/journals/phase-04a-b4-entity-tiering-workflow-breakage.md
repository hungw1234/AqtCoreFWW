# Phase-04a B4: Workflow Event Payload Breakage Embedded in Tiering Refactor

**Date**: 2026-05-03 14:12
**Severity**: High
**Component**: Domain entities (forms/workflow/organization); integration events (workflow 6/forms 3)
**Status**: Shipped; breaking changes in Phase-4b queue

## What Happened

Tiering refactor for 29 entities (forms 7, workflow 19, organization 3) + 9 event renames shipped successfully at Domain layer. But **6 workflow event payloads flipped parameter types mid-stream**, embedding structural breaking changes into event subscribers — they won't surface until Phase-4b handler refactor.

Example: `BranchCreatedEvent` parameter changed from `IReadOnlyList<Guid> branchIds` (internal IDs, pre-rebase) to `IReadOnlyList<long>` (post-rebase). Any handler deserializing old queued events fails at runtime with type mismatch. Worse: event schema lives in persisted outbox; old events sit in the dead-letter queue silently rotting until someone notices missing updates.

## The Brutal Truth

This is sloppy. We rebased entity PK relationships from Guid to long across workflow entities, flipped the event payload types to match, committed it, and only **then** realized the handlers haven't been updated yet. In a multi-tenant production system with persistent outbox queues, this is a **disaster waiting to happen** at deploy time.

The frustrating part: we caught it AFTER shipping B4. The plan said "Application layer handler refactor deferred to Phase-4b," but it glossed over the fact that **event consumer signatures change**. Handlers don't recompile — they silently fail to deserialize. No test caught it because the Infra layer doesn't compile yet.

## Technical Details

### Breaking Changes Embedded

**6 workflow events payload type-flips:**
- `BranchCreatedEvent`: `IReadOnlyList<Guid> childrenIds` → `IReadOnlyList<long>`
- `ParallelApprovalTaskCreatedEvent`: approval task ID param type flipped Guid→long
- `SlaBreachedEvent`: internal ID refs rebased
- `WorkflowTransitionedEvent`: state/branch ID params rebased
- `WorkflowInstancesMigratedEvent`: payload structure adjusted
- `BranchJoinCompletedEvent`: similar changes

**Method signature additions (breaking):**
- `WorkflowDefinition.Clone()`: now requires `IGuidGenerator gen` param (all 5+ App-layer callers must update)
- `WorkflowInstance.ApplyTransition/ApplyParallelJoinTransition/RecordMigration()`: same `IGuidGenerator gen` injection

### Why It Sneaked Through

1. **Test gap**: Architecture.Tests project won't compile until Infra refactor (Phase-4b) — event payload violations not caught by IL rules
2. **Outbox doesn't deserialize in unit tests**: Handlers aren't invoked during Domain-only compile pass. Event schema contracts live dormant.
3. **Plan omission**: Phase-04a plan said "handler refactor Phase-4b" but didn't detail the event payload consequence. We assumed event structure was immutable.

### WorkflowVersionMigrationItem Deviation

T3 plain-class preserved mutable state methods: `MarkSucceeded()`, `MarkFailed()`, `MarkSkipped()`. This violates the "append-only" T3 spirit but honors the audit trail contract — version migration IS a stateful journey (pending→success/failed/skipped is material to domain logic). Phase-3 R3 rule passes because `MarkDeleted()` is never exposed.

Decision was defensible but under-documented. Future T3 decisions need a red-team question: "Is this truly append-only or is mutation a domain invariant?"

## What We Tried

1. **Naive rebase approach**: Flip entity PKs from Guid→long, then auto-update all references (FK types, event payloads, etc.). Assumption: if Domain compiles, event contracts are safe.
   - **Failed expectation**: Domain compile ≠ event handler safety. Events are async; deserialization happens at Infra layer (Phase-4b).

2. **Allowlist architectural validation**: Added forms/workflow/organization to test allowlist, expecting tests to surface payload type violations.
   - **Failed reason**: Tests can't compile due to 500+ Infra errors from Phase-3 handler changes. Test suite blocked.

3. **Manual grep for event callers**: Searched for `BranchCreatedEvent` usages to count handler impact.
   - **Found**: 6 handlers consuming old payload shape; all require refactor Phase-4b.
   - **Lesson**: Should have automated this; manual grep scales poorly across 8 modules × ≥6 events.

## Root Cause Analysis

**Three failures converged**:

1. **Plan underspecified the "event contract stability" concern**: Phase-04a phase file listed "9 event renames" but didn't distinguish between safe renames (property rename `EventId→MessageId`) and unsafe changes (parameter type flip due to entity rebase). All lumped together as "distribute per-module."

2. **Test gate abandoned mid-phase**: Phase plan originally said "per-batch test gate;" feedback loop noted "full-solution build faster." We abandoned per-batch validation thinking Domain compile was sufficient. It isn't — event serialization is a cross-layer concern.

3. **Entity-first refactoring mindset**: We thought of this as a Domain refactor. The event payloads are **data contracts** that outlive code. Changing them requires coordinated migration (e.g., versioned event types, dual-deserialization, dead-letter rotation). We treated events like entity fields.

## Lessons Learned

1. **Event contracts are data contracts, not code contracts**: Changing an event payload parameter type is equivalent to adding a schema migration to a database table. Requires version bump + consumer adaptation. Do NOT embed payload type changes into "event rename" batches.

2. **Architecture tests must run mid-phase, not end-of-phase**: The test suite catching payload violations is worth the 5-min per-batch compile time. Stopping tests "because Infra errors" defeats the purpose — we need IL rules catching cross-layer contracts.

3. **Red-team structural type changes**: When rebasing entity PKs, explicitly ask: "Which event payloads reference this entity ID? Are those event types published/subscribed externally?" If yes, spike a versioning strategy first.

4. **WorkflowVersionMigrationItem is a pattern debt**: T3 plain-class that mutates (MarkSucceeded/Failed/Skipped) breaks the append-only invariant philosophically, even if Phase-3 R3 rule passes technically. Future T3 entities need explicit design gate: "append-only vs. stateful" decision documented upfront.

## Next Steps

**Phase-4b handler refactor (blocking)**:
- Update 6+ workflow event subscribers to handle new parameter types
- Add version gate: old events fail gracefully with logged warning (don't crash outbox worker)
- Update WorkflowDefinition.Clone() + ApplyTransition signatures across 5+ callers
- **New subtask**: Spike event versioning strategy (deferred Phase-5) to prevent recurrence

**Finalize (Phase-4a endgame)**:
- Re-enable Architecture.Tests suite after Infra rebase; run full validation on all 15 modules
- Grep for pre-existing Rule1 (Guid.NewGuid in events) + Rule5 (DateTimeOffset CreatedAt) violations in files/identity — document deferred phase

**Documentation**:
- Update `docs/design-guidelines.md` Event Versioning section with "parameter type changes = major event version bump" rule
- Add to `CLAUDE.md` MEMORY: `feedback-event-contracts-are-data-contracts.md`

---

**Unresolved Questions:**
- How many production outbox records carry old BranchCreatedEvent payload shape? (answered post-deploy via telemetry)
- Should workflow event versioning retroactively dual-deserialize (v1 Guid, v2 long) or hard-fail old messages? (defer Phase-5 migration planning)

