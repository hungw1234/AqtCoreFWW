# Phase-04a B3: Entity Rebase Domain Pass — Infra Cascade Ahead

**Date**: 2026-05-03 11:44
**Severity**: Medium
**Component**: audit/files/systemparams Domain — entity tiering refactor
**Status**: Resolved (Domain layer only)

## What Happened

Rebased 18 entities across 3 modules (audit: 11, files: 5, systemparams: 2) from Guid PKs to tiered structure (T1 generic-collapse, T2 PublicId, T3 plain-class). All Domain projects compile clean. Full solution: 195 errors in Infra layer—expected Phase-4b scope, not a surprise.

## The Brutal Truth

Domain layers passing while Infrastructure is hemorrhaging errors feels like a hollow win. The fact that Architecture.Tests can't even compile (blocked by handler layer) means we're shipping half a refactor and hoping downstream engineers don't rage-quit applying 200+ fixes. The commit looks clean because we deferred all the pain.

## Technical Details

**Domain builds**: audit 1.37s, files 1.26s, systemparams 5.19s ✅
**Full solution**: 195 errors all in Application + Infrastructure layers
**Architecture.Tests**: Cannot compile (transitively depends on infra handlers using old Guid FK types)
**Event payload breakage**: FileUploadedEvent, FileQuarantinedEvent changed `FileId FileId` → `Guid FilePublicId`—any subscriber expecting struct param will fail at runtime.
**Factory signature break**: RecordLifecycle.Create() dropped `createdBy` param (plain-class has no SetAuditCreate)

## What We Tried

- Per-batch compile gate (Domain only): works as design
- Deferred EF configurations to Phase-4b: consistent with B1/B2 precedent
- Updated allowlist to include audit/files/systemparams: ready for Architecture.Tests revival once infra compiles

## Root Cause Analysis

This is by design, not failure. Phase-4a strategy = Domain-only layer boundary per batch; handlers refactored Phase-4b in bulk. Cost: 195 stacked errors that look scary but are mechanical (old Guid FK types → new long FK types in handlers). The real question: **did we miss any breaking signature changes that subscribers can't handle at runtime?**

## Lessons Learned

1. **Event payload changes are silent killers.** FileUploadedEvent param type flip (struct→Guid) won't fail at compile; it'll break event deserialization at runtime unless subscribers are refactored. Documentation-level gotcha—next batch MUST grep all event subscribers before assuming Phase-4b is mechanical.

2. **IAggregateRoot on plain-class is incompatible.** AiPromptTrace was marked IAggregateRoot but plain-class form has no `_domainEvents` list. If any handler published via that entity, Phase-4b will discover a silent publish-to-void. Scout for `AiPromptTrace.DomainEvents.Add()` calls immediately.

3. **Factory method signature changes cascade hard.** RecordLifecycle.Create(gen, fileRecordId) dropping `createdBy`—this pattern repeats across all 10 T3 plain-classes. B4/B5 batches will have identical cascade. Phase-4b handlers need a coordinated sweep.

4. **Architecture.Tests deferral trades compile-time visibility for late-stage discovery.** We'll see IL rule violations only when infra compiles. That's acceptable, but it means Phase-4b opens with simultaneous entity + handler + test fixes—complexity spikes.

## Next Steps

1. **Phase-04a B4** (forms/workflow/organization): Refactor 29 entities, expand allowlist. Expect handlers to fail same way.
2. **Pre-Phase-4b scout** (critical): Grep `AiPromptTrace.DomainEvents`, `FileUploadedEvent` subscribers, RecordLifecycle.Create callsites. Document breaking changes explicitly in Phase-4b plan.
3. **Architecture.Tests revival strategy**: Batch-4 or batch-5 should unblock compile. Port IL rules incrementally (8 rules / 6 batches = ~1 per batch). First rule priority: factory injection pattern (IGuidGenerator param).
4. **Phase-4b plan template**: Explicit handler cascade fix + Architecture.Tests verification gate before version bump.

---

**Status**: RESOLVED (Domain layer); Infra errors expected deferred work

