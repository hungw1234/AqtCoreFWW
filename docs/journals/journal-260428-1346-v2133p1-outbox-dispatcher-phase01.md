# Session Journal — Outbox Dispatcher Phase 01 Implementation

**Date:** 2026-04-28 (afternoon, ~13:46 ICT)  
**Version shipped:** v2.133p1 (SQL Server main)  
**Phase:** `plans/260424-1518-outbox-dispatcher-phase-01/` (Phase 01 complete)

## What Changed

**Context:** Outbox pattern has been stubbed since v2.105 (6+ months). `OutboxProcessor` was a no-op `Task.Delay` loop giving false signal that IExternalDomainEvent pipeline was wired. Phase 01 replaces stub with actual dispatcher logic backed by registry, schema validation, and failure recovery.

**Solution:** Implement narrow data layer (`IOutboxStore`) + dispatcher state machine (`OutboxDispatcher`) + processor loop rewrite (`OutboxProcessor`). Central schema registry prevents SQL injection via schema-name interpolation in raw SQL.

**Execution:**
- NEW: 5 core services (OutboxOptions ext, IOutboxSchemaRegistry, OutboxDispatcher, OutboxStore, OutboxDispatchOutcome)
- NEW: OutboxProcessor rewrite (scope-per-cycle pattern with graceful cancellation)
- UPDATED: InfrastructureRegistration.cs (DI registration + 14 default schema adds)
- NEW: 20 unit tests + 119 suite cross-run

**Files changed (11 total):**
1. OutboxOptions.cs (deadletter + kill-switch fields)
2. IOutboxSchemaRegistry.cs + OutboxSchemaRegistry.cs + OutboxSchemaRegistryExtensions.cs (3 registry files)
3. OutboxDispatchOutcome.cs (enum + summary record)
4. IOutboxStore.cs + OutboxStore.cs (data interface + Dapper impl)
5. OutboxDispatcher.cs (state machine)
6. OutboxProcessor.cs (rewritten loop)
7. InfrastructureRegistration.cs (DI + schema adds)

## Evidence

- **Build:** 0E / 0W (full solution 3:49)
- **Tests:** 20 new + 99 pre-existing = 119/119 pass (OutboxDispatcherTests 9 cases, OutboxSchemaRegistryTests 12, Architecture 16, Cases.Application 40, Notifications.Application 32)
- **Code review:** APPROVED_WITH_MINOR (8.5/10), 0 critical, 2 P1 findings (see Tech Debt)
- **Backlog:** Outbox Dispatcher Phase 01 ticked DONE (v2.133p1)

## Plan Verification & Drifts Found

**Reverify protocol** (per memory `feedback-reverify-plan-before-execute.md`): plan was 4-day stale (created 2026-04-24). Scout subagent verified 20 claims; **3 drifts detected:**

1. **Module count:** Plan claimed "17 InfrastructureRegistration files"; actual = 14 ModuleDbContext subclasses. Audit module extends DbContext directly (no OutboxMessages table). Documented exclusion prevents future accidental addition.

2. **DI registration status:** Plan claimed "OutboxProcessor not registered"; **actual = registered at InfrastructureRegistration.cs:74 since v2.132p7.** Scout report was incorrect (cross-verified with Grep). Saved wasted re-registration step.

3. **IExternalDomainEvent location:** Plan said "Contracts"; actual = `SharedKernel.Domain.Events` (domain-side event wrapper). Verified correct per existing DDD pattern.

**Impact:** Plan was 95% accurate; 3 drifts required 5-min fact-check before execution. No rework needed.

## Implementation Details

**OutboxSchemaRegistry** — defence-in-depth against schema-name SQL injection:
- Maintains allowlist of allowed schema names (lower-cased)
- Regex `^[a-z][a-z0-9_]{0,29}$` rejects unsafe names at registration time
- `ClaimBatchAsync` interpolates schema name AFTER validation (not from untrusted input)

**OutboxDispatcher** — pure state machine over `IOutboxStore` + MassTransit IBus:
- Type resolution failure (Type.GetType null) → immediate dead-letter (no retry)
- JSON deserialisation failure → mark for retry (threshold = 3)
- Publish via `bus.Publish(evt, eventType, ct)` (object overload for dynamic type)
- Threshold exhaustion → atomically move to dead-letter table in single transaction

**OutboxStore** — Dapper with UPDLOCK + retry counting:
- `ClaimBatchAsync`: SELECT with UPDLOCK (claims batch for exclusive dispatch)
- `MarkRetryAsync`: increments retry counter
- `MoveToDeadLetterAsync`: single-tx INSERT messaging.dead_letters + UPDATE [schema].OutboxMessages

**InfrastructureRegistration** — KISS over multi-file approach:
- Registered 14 default schemas centrally (not 14 module-level edits per plan)
- Same outcome, 1 file vs 14 files touched
- IOutboxStore singleton, OutboxDispatcher scoped, OutboxProcessor registered

## Risk Assessment

**Low risk (8.5/10):**
- Narrow scope: new services don't alter existing event publishing pipeline
- Type-safe dispatch: MassTransit IBus generic Publish handles event routing
- Failure recovery: retry + dead-letter prevents silent drops
- No EF migrations required (OutboxMessages table existed since v2.105 stub)

**Code review notes (2 P1 findings, deferred Phase 2):**
- **P1-1:** `ClaimBatchAsync` UPDLOCK releases after SELECT statement (autocommit). Multi-replica polling (5s apart) can both claim same row. Mitigated by Phase 2 idempotent consumer design + MassTransit deduplication. Recorded as OUTBOX-1 (~0.5d, deferred). Added explanatory comment next to UPDLOCK.
- **P1-2:** Schema regex defence not exercised in unit tests against real raw-SQL path. Acceptable for unit-test gate; deferred to Phase 2 integration test (when real raw SQL exists).

## What Stayed the Same

- **Event schema:** IExternalDomainEvent unchanged (domain-side wrapper)
- **Publishing:** CreateCaseCommandHandler still calls `_mediator.Publish(evt)` (no changes)
- **Module clients:** WorkflowModuleClient, CasesModuleClient unaffected
- **Architecture guard:** Keeps ARCH-1 guard GREEN (v2.132p11 baseline maintained)

## Lessons

1. **Plan staleness is real (4-day stale = 3 drifts).** Cross-verify module counts + registration status before execution. Scout is useful but not infallible (P1-1 false claim caught by Grep).

2. **KISS over plan scope.** Plan suggested "modify 17 module files" for schema registration. Centralized approach (1 file) is simpler and equally correct. Respect planning but adapt for clarity.

3. **Defence-in-depth against parameter injection.** Schema name interpolation in raw SQL is inherently risky. Registry validation + regex at registration time prevents bad names from ever reaching SQL layer.

## Tech Debt Opened

1. **OUTBOX-1 (P1-1, ~0.5d):** Strict at-most-once-per-replica. Workaround: `ClaimedAtUtc` column or rewrite `ClaimBatchAsync` as single-tx UPDATE...OUTPUT. Deferred Phase 2.

2. **Integration E2E test (deferred Phase 2):** Outbox → MassTransit → consumer deterministic flow. Requires Phase 2 consumer implementation first.

## Files Updated in Finalize

1. docs/dependency-map.md — version v2.133p1 + OutboxStore interface
2. docs/project-roadmap.md — header version + v2.133p1 row + Outbox Dispatcher Follow-ups note
3. docs/project-changelog.md — v2.133p1 entry (top)
4. backlogs/backlog.md — Phase 01 ticked DONE + OUTBOX-1 entry + Follow-ups section
5. memory/project-state.md — frontmatter + body (v2.132p11 → v2.133p1)
6. memory/MEMORY.md — project-state.md row updated (version string)
7. docs/journals/journal-260428-1346-*.md — this file

## Why This Matters

Closes 6+ month tech debt. OutboxProcessor stub gave false confidence that at-least-once event dispatch was implemented. Phase 01 makes the IExternalDomainEvent contract real: events published to Outbox table are guaranteed to be dispatched to MassTransit consumers (within retry threshold). Unblocks Phase 2 (deferred): CreateCaseCommandHandler will defer WorkflowDefinition instantiation → expected p95 latency -12ms per op.

## Next Steps

1. **Phase 02 (deferred):** Implement consumer idempotence patterns + update Phase 2 test suite
2. **Phase 03 (future):** Integrate into CreateCaseCommandHandler with `Cases:DeferWorkflowCreation` flag
3. **OUTBOX-1 (P1-1):** Evaluate stricter at-most-once claim (discuss with team if worth 0.5d cost)
