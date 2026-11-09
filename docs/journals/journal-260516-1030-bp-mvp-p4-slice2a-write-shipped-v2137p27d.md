# Journal — BP MVP P4 Slice 2A-Write shipped (v2.137p27d)

**Session:** 2026-05-16 09:15 → 10:30 ICT (~75 min)
**Branch:** main · **Pushed:** origin/main `b60c8fac` + `4d5b0136`
**Ship counter:** #2 of 2026-05-16 (ship-#2-of-day band respected)
**Carry-in:** Slice 2A-Read shipped clean; advisor-reconciled approach from 2A-Read horizon

## Flow

1. Slice 2A-Read journal tagged; fresh carry context from prior session (09:15). 2A-Write scope locked: 4 command handlers (Create/Update/Activate/Deactivate) + OutboxInterceptor event routing + cache wrap/invalidation + controller +4 endpoints + 2 request DTOs. Estimated 6-8h; this session throttled to ~1.5h ship.
2. Advisor gut-check pre-cook: MessageBus vs Outbox semantics. Plan said `IMessageBus.PublishAsync` for Activate/Deactivate events. Advisor two-grep sweep (`OutboxMessages` infra + `IAuditService` consumers) surfaced prior art: Forms `SubmitForm` has event-emission but no Outbox comment ("no Outbox today" + direct MassTransit). Flipped approach to `IExternalDomainEvent` records + existing `OutboxInterceptor` (transactional guarantee > best-effort pub). Cost: ~5 min advisor questions + ~10 min verification reads. Avoided: silent loss of transactional boundary.
3. Scout sweep ~8 min: AuditBehavior CommandVerbs arrays omitted Activate/Deactivate (code-review M-1 catch incoming). ExtractAction truncates to 20 chars; existing array entries inconsistent. IMemoryCache plumbing found in Rules module cache keys pattern. Outbox schema verified in EF Migrations. Shared audit enum patterns in SharedKernel.
4. Cooked 4 command handlers + OutboxInterceptor event routing + BusinessProcessCacheKeys DRY helper + controller +4 endpoints + 2 DTOs. Total: ~15 files touched, ~420 LOC net. Idempotent Activate/Deactivate asserted in Domain tests (no double-event emission).
5. Code-review caught **M-1 (silent compliance bug):** `AuditBehavior.CommandVerbs` arrays (Domain + Infrastructure) duplicated and both omitted Activate/Deactivate. Audit rows shipped before but `action` column would be truncated/invalid for new verbs. Fix: DRYed two arrays → one `AuditBehavior.CommandVerbs` constant in SharedKernel + 2 regression tests (`AuditBehaviorTests` +2 facts asserting concrete `action="Activate"` / `resource="Probe"` shape). Project-wide compliance improvement not just BP.
6. Code-review also caught **M-2 (layering violation temp):** I added `using Microsoft.EntityFrameworkCore;` to `CreateBusinessProcessCommandHandler` to wrap `DbUpdateException` for TOCTOU 409. EF in Application layer = arch-test violation. Reverted; deferred TOCTOU 409 to P4-2A-Write-FU1 with explicit design: new repo method `AddOrFailOnDuplicateAsync(...)` returning discriminated outcome (Application stays EF-free).
7. Code-review L1/L2/L3 fixups folded inline: L1 (DRY cache-keys helper), L3 (cache TTL comment), L4 (outbox schema validation comment). L2 deferred (TOCTOU 409 needs repo-layer design).
8. Tester: BP Application +15 tests (4 handler files + 2 Create partial-update permutations) = **22/22 PASS**; BP Domain idempotency extended = **19/19 PASS**; BP Infrastructure 2/2; Architecture 97/97; PermCodes 10/10; **AuditBehaviorTests 7→9** (M-1 fix + 2 verb regression probes). Build **0E/0W**.
9. Code-reviewer: **9.3/10 APPROVE_WITH_NITS** (M-1 folded inline + 9.5+ estimated post-fixup, M-2 deferred to FU1). Zero critical/high. L2 (TOCTOU 409) + L3 (WfDefId existence check, Slice 2B dep) filed as FU.
10. project-manager + docs-manager spawned in parallel — synced roadmap progress, changelog v2.137p27d, backlog, dependency-map; P4 Slice 2C dependency unlock noted.
11. Two commits: `b60c8fac` (feat) +420 LOC + `4d5b0136` (docs sync) pushed to origin/main.

## Decisions

- **D1 (Outbox vs MessageBus):** Advisor's cross-context grep (OutboxMessages + IAuditService) revealed prior art (Forms SubmitForm). Transactional outbox semantics (same-txn event durability) > best-effort async publish. `IExternalDomainEvent` records via `OutboxInterceptor` chosen, not raw `IMessageBus`.
- **D2 (event discipline on idempotent ops):** Activate/Deactivate are idempotent toggle operations. Domain tests assert no double-event emission on idempotent calls (state unchanged → no event added). Aggregate `AddDomainEvent` guarded on real state change.
- **D3 (AuditBehavior DRY recovery):** M-1 fix centralized two duplicated CommandVerbs arrays in SharedKernel. Every new common verb addition now has paired regression test asserting concrete `action` / `resource` shape. Codified: verb audit compliance is test-protected project-wide.
- **D4 (EF layering boundary):** Attempted DbUpdateException catch in Application layer (M-2 violation). Reverted; deferred TOCTOU 409 design to P4-2A-Write-FU1 with repo-layer outcome method. Application stays EF-free; arch-test clean.
- **D5 (cache invalidation scope):** IMemoryCache `bp:{tenantId}:active` (broad prefix) invalidated on every mutation (Create/Update/Activate/Deactivate). Pattern mirrors Rules module. No partial-key invalidation (YAGNI).

## Lessons

- **Lesson 1 (advisor cross-context wins):** Plan optimized for vertical-atomic features; didn't surface prior art precedent (Forms SubmitForm). Advisor's two grep questions (OutboxMessages + consumers) flipped a wrong path in ~15 min. Pattern: when uncertain between architectural choices, advisor-first grep-sweep is cheaper than mid-code revert.
- **Lesson 2 (audit compliance as regression test, not config):** M-1 shipped weeks ago when AuditBehavior landed — no module had Activate/Deactivate commands until BP. Audit rows were captured but `action`/`resource` columns truncated/useless. Fix adds 2 tests asserting concrete shape. Codify: **every new CommandVerb addition = paired regression test in AuditBehaviorTests**, not just "update constant."
- **Lesson 3 (layering violation = cheap catch):** Briefly violated arch boundary (EF in Application). Code-review M-2 flag instant; reverted + deferred to proper repo-layer design. Arch-test would have caught at build, but human review faster.
- **Lesson 4 (idempotent operation event discipline):** Aggregate state-machines that are idempotent (Activate/Deactivate toggles) need explicit Domain test assertion: state-unchanged → no event emitted. Tests double-check toggle twice in same txn → verify single event only. Pattern: audit + Outbox make event emission load-bearing; test it.
- **Lesson 5 (DRY-before-copy):** Slice 2A-Read had hardcoded `bp:{tenantId}:active` literal in code; Write would have copied to 4+ sites. L1 fixup pre-emptively extracted `BusinessProcessCacheKeys` helper. Cost: 5 min refactor. Avoided: cache-key typo spreading across module.

## Carry-over

- **Slice 2B** (~1.3d): `IBusinessProcessQueryClient` 5 methods + Forms cross-module client integration for `GetIntakeFormSchema`. Unblocks P6/P7/P8 dependent modules. New endpoint needs schema dependency validation (P3 lesson on cross-schema raw SQL).
- **Slice 2C** (~0.3-0.5d Medium-risk):** 3 runtime endpoints (Start Run / Transition / Submit State Form) + ambient `TransactionScope` coordination + integration tests. Highest complexity in P4.
- **P4-2A-Write-FU1** (Low, ~1h): TOCTOU 409 retry/duplicate Name check. New repo method `AddOrFailOnDuplicateAsync(...)` returning discriminated outcome (keep Application EF-free).
- **P4-2A-Write-FU2** (Low): WorkflowDefinitionId existence check at Create time. Cross-module dependency (needs IWorkflowModuleClient available in Slice 2B).
- **P4-2A-Read-FU** (Low, carries from 2A-Read): L-1 LIKE-escape `_`/`[` cross-module sweep, L-3 ListActive 100-cap documentation, L-6 SQL emission integration test.
- **P3-FU1** (~3-4h, still open): TenantIsolation enforcement via fixture-based tenant-mismatch tests (R13 guard already in place, but no explicit test harness yet).

## Tool incidents

- **Edit tool multi-step atomicity:** Mid-sequence edit to `AuditBehavior.CommandVerbs` array in Infrastructure file succeeded; same session later edit to Domain file's array succeeded. No UTF-8 corruption or partial-fail this session (prior feedback loop codified fallback to Write).

## Files (~20 touched)

**Source (4 new):**
- `src/modules/businessprocess/AqtCoreFW.BusinessProcess.Application/Commands/CreateBusinessProcess/{Command,Handler}.cs`
- `src/modules/businessprocess/AqtCoreFW.BusinessProcess.Application/Commands/UpdateBusinessProcess/{Command,Handler}.cs`
- `src/modules/businessprocess/AqtCoreFW.BusinessProcess.Application/Commands/ActivateBusinessProcess/{Command,Handler}.cs`
- `src/modules/businessprocess/AqtCoreFW.BusinessProcess.Application/Commands/DeactivateBusinessProcess/{Command,Handler}.cs`
- `src/modules/businessprocess/AqtCoreFW.BusinessProcess.Application/Cache/BusinessProcessCacheKeys.cs` (DRY helper)
- `src/modules/businessprocess/AqtCoreFW.BusinessProcess.Application/Dtos/CreateBusinessProcessRequest.cs`
- `src/modules/businessprocess/AqtCoreFW.BusinessProcess.Application/Dtos/UpdateBusinessProcessRequest.cs`

**Tests (3 modified, +15 facts):**
- `tests/unit/AqtCoreFW.BusinessProcess.Application.Tests/Commands/CreateBusinessProcessCommandHandlerTests.cs` (4 facts)
- `tests/unit/AqtCoreFW.BusinessProcess.Application.Tests/Commands/UpdateBusinessProcessCommandHandlerTests.cs` (5 facts, including 2 partial-update permutations)
- `tests/unit/AqtCoreFW.BusinessProcess.Application.Tests/Commands/{Activate,Deactivate}BusinessProcessCommandHandlerTests.cs` (6 facts, idempotency + event-emission discipline)
- `tests/unit/AqtCoreFW.SharedKernel.Tests/Audit/AuditBehaviorTests.cs` (+2 facts for M-1 verb regression)
- `tests/unit/AqtCoreFW.BusinessProcess.Domain.Tests/Aggregates/BusinessProcessAggregateTests.cs` (extended idempotency assertions)

**Modified (infrastructure/controller):**
- `src/modules/businessprocess/AqtCoreFW.BusinessProcess.Presentation/Controllers/BusinessProcessesController.cs` (+4 endpoints)
- `src/AqtCoreFW.slnx` (no new entries; all projects pre-created)
- `src/shared/AqtCoreFW.SharedKernel/Audit/AuditBehavior.cs` (M-1 DRY + verb array centralization)
- `src/modules/businessprocess/AqtCoreFW.BusinessProcess.Infrastructure/Migrations/` (Outbox registration verified, no new migrations)
- `backlogs/backlog.md`, `docs/project-changelog.md`, `docs/development-roadmap.md`, `docs/dependency-map.md`, `plans/260420-.../plan.md`

## Code-Review Summary

**Reviewer:** code-reviewer agent  
**Score:** 9.3/10 APPROVE_WITH_NITS  
**Severity Breakdown:** 0 Critical, 0 High, 2 Medium, 5 Low

**Medium findings (folded inline + deferred):**
- M-1: `AuditBehavior.CommandVerbs` array duplicates omitted Activate/Deactivate → **FOLDED:** DRY into SharedKernel constant + 2 regression tests asserting concrete `action`/`resource` shape. Project-wide compliance recovery.
- M-2: Temporary EF usage in Application layer (DbUpdateException catch) → **DEFERRED:** P4-2A-Write-FU1 new repo method returning discriminated outcome (keep Application EF-free per arch-test).

**Low findings (folded):**
- L1: Cache keys hardcoded → **FOLDED:** Extracted `BusinessProcessCacheKeys` DRY helper.
- L3: Cache TTL no comment → **FOLDED:** Added JSDoc `/** 5-minute active cache TTL, invalidated on mutation */`.
- L4: Outbox schema not verified in code comment → **FOLDED:** Added integration test scope note.

**Deferred to FU:**
- L2 (TOCTOU 409): Needs repo-layer discriminated-outcome method design.
- L5 (WfDefId existence check): Cross-module dependency, Slice 2B scope.

---

**Status:** DONE

