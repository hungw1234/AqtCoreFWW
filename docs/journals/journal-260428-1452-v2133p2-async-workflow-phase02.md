# Session Journal — Async Workflow Provisioning Phase 02

**Date:** 2026-04-28 14:52 ICT  
**Version shipped:** v2.133p2 (SQL Server main)  
**Phase:** `plans/260424-0922-v2133-async-workflow-publish/phase-02-workflow-defer-integration-event.md` (Phase 02 complete)  
**Predecessor:** `journal-260428-1346-v2133p1-outbox-dispatcher-phase01.md`

## What Changed

**Context:** Phase 01 (v2.133p1, shipped same morning) implemented OutboxDispatcher polling loop. Phase 02 wires the consumer: `CaseCreatedWorkflowRequestedEvent` raised by Cases.Application when creating a Case (feature flag + config flag both ON) → captured by OutboxInterceptor into `cases.OutboxMessages` → dispatched by Phase 01 Dispatcher → MassTransit → `CreateWorkflowInstanceOnCaseCreatedConsumer` in Workflow.Application for async instance creation. Removes ~12-15ms from CreateCase request path.

**Execution:**
- NEW: `CaseCreatedWorkflowRequestedEvent` in `SharedKernel/Domain/Events/` (IExternalDomainEvent, JSON serializable)
- NEW: `CreateWorkflowInstanceOnCaseCreatedConsumer` in `Workflow.Application/EventHandlers/` (7 test cases: happy, idempotency, tenant isolation, retry semantics)
- UPDATED: `CreateCaseCommandHandler` — branch on `Cases:DeferWorkflowCreation` config flag (default OFF, zero behavior change)
- UPDATED: `Program.cs` — added `Workflow.Application` assembly to `AddConsumers` registration
- NEW: 12 unit tests (7 consumer + 4 handler matrix + shared tenant isolation)
- UPDATED: 6 docs artifacts (dependency-map, changelog, roadmap, backlog, project-state, MEMORY)

## Critical Decisions

1. **Event lives in `SharedKernel/Domain/Events/` not `Contracts`** — Contracts.csproj is "pure POCO no PackageReference"; pulling in SharedKernel for `IExternalDomainEvent` would break that promise. Used precedent of `PasswordResetRequestedEvent` (also internal-only but lives in SharedKernel for reuse). Avoided creating new `Cases.Contracts.Events` project (YAGNI).

2. **EventId/OccurredAt use `{ get; init; }` not `{ get; }`** — JSON serialiser (System.Text.Json) must round-trip these through Outbox payload. Positional record params don't auto-support init-only; `{ get; init; }` fixes deserialization. PasswordResetRequestedEvent uses `{ get; }` because it's IInternalDomainEvent (in-process, no serialization).

3. **Registration: reused existing `Program.cs` list** instead of creating `WorkflowInfrastructureModule.AddWorkflowConsumers` static helper. KISS — one line (added assembly to existing `AddConsumers(...)` call) beats new public API surface. Plan called for helper; opted for simpler approach.

4. **No new `Cases.Application` PackageReference** — initially added `Microsoft.Extensions.Configuration.Abstractions` thinking handler needed explicit dep. Central Package Management (NU1010) caught it. Removed; IConfiguration arrives transitively via SharedKernel's `Microsoft.AspNetCore.App`. Same for MassTransit logging — only `MassTransit` package added, ILogger transitive.

## Implementation Details

**CaseCreatedWorkflowRequestedEvent** — sealed record implementing `IExternalDomainEvent`:
- Payload: `CaseId`, `TenantId`, `CreatedBy`, `DefinitionKey` (IDs + string only — NO PII per ArchUnit rule)
- `EventId` + `OccurredAt` use `init` accessor for JSON round-trip safety
- XML doc emphasizes: lives in SharedKernel (shared type, avoids cross-module ref), OutboxInterceptor captures on SaveChanges, Dispatcher publishes to consumer

**CreateWorkflowInstanceOnCaseCreatedConsumer** — MassTransit `IConsumer<>` implementation:
- **Idempotency:** probes `IWorkflowModuleClient.TryResolveInstanceForEntityAsync(EntityType, CaseId, TenantId)` first → skip if exists (safe for replays)
- **Failure:** throws `InvalidOperationException` on definition-not-found → MassTransit retries 5s/30s/5m (absorbs seeding window) → dead-letter after exhaustion
- **Tenant isolation:** uses payload `TenantId` (never ambient context, async dispatcher runs outside per-request scope)
- **Observability:** logs skipped + created instances with CaseId, InstanceId, duration

**CreateCaseCommandHandler changes:**
- Inject `IConfiguration` (for config flag binding)
- After `repository.AddAsync(@case)`: if `delegationOn && deferWorkflowCreation` → add event to aggregate
- Event captured by OutboxInterceptor during SaveChanges (same tx as Case insert)
- Else (legacy): existing sync `workflowClient.CreateInstanceByDefinitionKeyAsync` path unchanged
- Always preserve `delegationOn && !deferWorkflowCreation` legacy path (zero behavior change when flag OFF)

## Tests: 12 Total (7 consumer + 4 handler + 1 arch)

**Consumer (7 tests):**
- Happy path: no existing instance → creates
- Existing instance: skips (idempotent)
- Two back-to-back deliveries: creates once (simulates at-least-once dispatcher retry)
- Tenant isolation: passes payload TenantId to client, not ambient
- Definition not found: rethrows for MassTransit retry
- TryResolve throws: propagates (transient DB, no swallow)
- Honors CancellationToken: reaches both client calls

**Handler (4 tests):**
- Defer flag ON: doesn't call sync client
- Defer flag ON: raises `CaseCreatedWorkflowRequestedEvent` on aggregate (snapshot pattern required)
- Defer flag OFF: doesn't raise event (legacy path preserved)
- Delegation OFF + Defer ON: neither sync nor async (prevents accidental outbox on legacy tenants)

**Tech debt:** Test for "existing instance" snapshot required special NSubstitute pattern (`Arg.Any<Case>()` captured in `.Returns(call =>` callback) because handler clears DomainEvents after publishing. Direct post-Handle inspection sees empty list.

## Errors & Lessons

**Failed first:** Handler test `Handle_DeferFlagOn_RaisesCaseCreatedWorkflowRequestedEventOnAggregate` — expected events on aggregate after Handle() returned, but list was empty (handler clears events post-publish). Fix: snapshot `case.DomainEvents` INSIDE the mock callback using `StrongBox<>` capture (NSubstitute `.Returns(call => { var c = call.Arg<Case>(); snapshot.AddRange(c.DomainEvents); ... })`). Lesson: test internal state changes require capture-at-mutation, not post-fact inspection.

**Config package surprise:** Plan assumed Cases.Application transitively had `Microsoft.Extensions.Configuration.Abstractions`. NU1010 built error initially looked like missing dep; actually CentralPackageManagement rejects redundant direct refs. Verified: IConfiguration DOES arrive transitively. Wasted ~2min adding/removing PackageReference; correct solution = inject and use existing transitive dep.

## Code Review & Build

- **Build:** 0E / 0W (full solution ~4:15)
- **Tests:** 605/605 pass (12 new + 593 pre-existing). Consumer tests all green. Handler defer matrix green. Arch guard `NoModule_Should_DirectlyReference_AnotherModule` GREEN (event in SharedKernel avoids cross-module project ref).
- **Code review:** APPROVED_WITH_MINOR (8.5/10 same calibration as Phase 01). 0 P0. 1 P2 follow-up: OUTBOX-1 TOCTOU window (two concurrent consumers both seeing "no instance" → both create). Mitigations documented (MassTransit dedup + eventual duplicate detection). Unique partial index `(EntityType, EntityId, TenantId) WHERE CompletedAt IS NULL` deferred to Phase-05. Appended to backlog.

## Finalize Artifacts

6 docs + 1 journal verified ✅:

| Artifact | Status | Evidence |
|----------|--------|----------|
| docs/dependency-map.md | ✅ | v2.133p2 header + CaseCreatedWorkflowRequestedEvent row (line 75) + consumer + config flag default=false |
| docs/project-changelog.md | ✅ | v2.133p2 entry (line 9) + 12 tests + 605/605 PASS + OUTBOX-1 P2 note |
| docs/project-roadmap.md | ✅ | Phase 02 status COMPLETE + v2.133p2 version bump (line 8) + Next Phase 03 outline |
| backlogs/backlog.md | ✅ | Phase 02 ticked DONE + OUTBOX-1 P2 entry added + Phase 03+ outline |
| memory/project-state.md | ✅ | Frontmatter + body: v2.133p2 description (line 4) + prior phases summary |
| memory/MEMORY.md | ✅ | project-state.md row updated with v2.133p2 version string |
| Session journal | ✅ | This file (journal-260428-1452-v2133p2-async-workflow-phase02.md) |

## Why This Matters

Phase 01 + 02 closes the async workflow provisioning track. `CreateCaseCommandHandler` now supports both paths:
- **Legacy (default, flag=OFF):** synchronous, block HTTP response on workflow instance creation (~12-15ms savings still available)
- **Async (flag=ON):** queue via Outbox, return HTTP 201 immediately, consumer handles async creation within ~10s

Expected p95 latency drop ≥12ms (20-25% improvement) when flag ON in Benchmark config. FE unaffected (WorkflowInstanceId still null in response, no breaking change).

## Lessons Learned

1. **Snapshot internal state at mutation point, not after.** Aggregate event-list inspection AFTER handler clears requires capture inside mock callback. Direct post-fact reference is empty.

2. **Transitive deps are real.** Configuration + Logging arrive via framework references. Don't redundantly add PackageReference if already available transitively. CentralPackageManagement enforces this.

3. **Precedent > new projects.** Both CaseCreatedWorkflowRequestedEvent + PasswordResetRequestedEvent are IExternalDomainEvent in SharedKernel (not module-local, not Contracts). Avoids creating new project surface (YAGNI). Consistency matters for future developers.

4. **KISS over complex registration.** Plan called for `WorkflowInfrastructureModule.AddWorkflowConsumers` static helper. Reality: one line in Program.cs (add assembly) is simpler, equally correct. Resist over-engineering.

## Next Steps

1. **Phase 03 (deferred):** FE flag-gating + real-time notification when WorkflowInstance created async
2. **OUTBOX-1 (P2, Phase-05):** Unique partial index on `(EntityType, EntityId, TenantId)` for durable at-most-once guarantee
3. **Observability (v2.134):** Prometheus `outbox_lag_seconds` metric + OpenTelemetry spans on dispatcher + consumer
4. **k6 rerun (observability track):** measure p95 delta with flag ON in Benchmark config (separate session, not blocking)

---

**Status:** DONE_WITH_CONCERNS  
**Summary:** Phase 02 async workflow provisioning shipped (v2.133p2). 12 new tests GREEN. 605/605 pass. Code-review APPROVED. Zero behavior change at deploy (config flag default OFF). Known TOCTOU window (OUTBOX-1) documented in backlog P2. Unblocks Phase 03 FE work.  
**Concerns:** OUTBOX-1 TOCTOU hardening deferred (trade-off: current mitigations sufficient for phase delivery, unique index worth ~0.5d future work). Confirmed acceptable with code reviewer.
