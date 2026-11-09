# Phase 02 Follow-up Hardening (v2.127p3)

**Date**: 2026-04-22 14:37+07
**Severity**: Medium
**Component**: Workflow.Application, Workflow.Infrastructure, SharedKernel (audit)
**Status**: Resolved

## What Happened

Post-code-review hardening pass on Phase 02 Cases Workflow (v2.127p2). Applied 3 targeted fixes from reviewer feedback: tenant filter info-leak on EntityTypeLookup, AuditBehavior resource ID extraction, and test layering enforcement. Smaller than v2.127p2 but essential—these close the diagnostic gaps that accumulated during feature-heavy phase.

## The Brutal Truth

The frustration: v2.127p2 shipped with sloppy test assertions. F1's initial `BeInRange(400, 499)` would've silently passed if RBAC intercepted the lookup before the new SQL filter. That's a testing antipattern we've made before—assertions so loose they don't actually prove the code path you added runs. This time the reviewer caught it. The real lesson is that we need tighter test specs in review, not after.

F3 (test layering) felt inevitable in hindsight: Application.Tests shouldn't reach Infrastructure impl for its test doubles. That it did revealed a lazy pattern in how we structured validator test support—registry stub lived in Infrastructure but validators imported it via Application.Tests. Moving it to the right layer forced test writers to own their doubles instead of spreading concerns across layers.

## Technical Details

**F1 — Tenant Filter (WorkflowDefinitionEntityTypeLookup)**

```csharp
// Interface signature changed:
Task<EntityType?> GetEntityTypeAsync(Guid definitionId, Guid tenantId, CancellationToken)

// SQL filters via tenant-fallback pattern (mirrors GetWorkflowDefinitionQueryHandler):
WHERE Id = @DefinitionId AND (TenantId = @TenantId OR TenantId = @GlobalTenantId) AND IsDeleted = 0
```

- Validator (`SaveDefinitionGraphCommandValidator:63`) passes `cmd.TenantId`—no ITenantContext coupling
- Integration test assertion tightened: `Should().BeOneOf(422, 400)` + body contains "not found" (forces SQL filter exercise)
- Exactly 1 caller—Grep confirmed before signature change

**F2 — AuditBehavior Resource ID Extraction**

```csharp
ConcurrentDictionary<Type, Func<object?, string?>> _resourceIdExtractors;

// BuildExtractor: reflects response.Value.Id on Result<TDto>
// Fallback: returns null gracefully on reflection failure
// Cost: one-time per DTO type; Audit layer tolerates best-effort
```

- Avoided marker interface (`IAuditableResponse`) which would churn 9 DTOs—reflection cache is pragmatic
- 2 unit tests: DTO with Id → extracted; DTO without → null

**F3 — Test Layering**

- Removed `<ProjectReference>` Application.Tests → Infrastructure
- Moved `EntityStatusEnumRegistryTests` to Infrastructure.Tests (right layer = right diagnostics)
- Embedded `FakeEntityStatusEnumRegistry` in validator test files (dict-backed, StringComparer.Ordinal)
- Application.Tests now owns test doubles—no cross-layer leak

## Metrics

- **Build**: 0W / 0E
- **Tests**: 644/644 pass
  - SharedKernel: 115
  - Workflow.Application: 425
  - Workflow.Infrastructure: 76
  - Integration (Workflow filter): 28
- **Code-review**: 9/10 APPROVED_WITH_MINOR; M1 applied post-review
- **Execution**: Estimated 2–3h, actual ~1.5h

## Lessons Learned

1. **Loose assertions hide implementation gaps** — `BeInRange(400, 499)` could pass without ever exercising the filter SQL. Test spec must force the exact behavior added. Lesson: reviewer should ask "does this test fail if I remove the feature?"

2. **Reflection caching beats DTO pollution** — Considered marker interface for audit response tracking; would touch 9 DTOs across modules. Reflection extractor + `ConcurrentDictionary` cache pays off once per type at zero DTO churn. Audit layer design already tolerates best-effort (null gracefully).

3. **Test layering enforces correct diagnostics** — When Application.Tests reached Infrastructure impl, registry bugs surfaced as Application test failures (false attribution). Moving tests to Infrastructure.Tests forced writers to own test doubles; failures now locate correctly.

4. **Scout callers before signature changes** — F1 changed interface signature. Single Grep pass on `IWorkflowDefinitionEntityTypeLookup` found 1 call site before edit. Avoided cascading breakages across validators.

## Next Steps

**v2.128 Target**: Phase 01 Outbox Dispatcher (MassTransit EF Outbox integration). Brainstorm round 2 planned to resolve 6 open architecture questions (PCS batching, consumer circuit-break strategy, dead-letter routing). Start date: pending design sync.

**Immediate**: Update `memory/project-state.md` version bump to v2.127p3, note workflow-validation hardening complete.
