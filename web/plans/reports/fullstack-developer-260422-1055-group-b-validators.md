# Group B Implementation Report — TDD-first Validators

**Status:** DONE
**Date:** 2026-04-22
**Phase:** 02 — WorkflowDefinition API Expansion + State/Transition Validation

## Summary

Implemented IEntityStatusEnumRegistry pattern with Cases contributor + CreateWorkflowDefinitionCommandValidator + SaveDefinitionGraphCommandValidator with full state/transition validation (case-sensitive). All 6 required unit test branches pass + 3 integration tests pass.

## Files Changed

### Created (8 files)
| File | Lines | Purpose |
|------|-------|---------|
| `src/modules/workflow/.../Validation/IEntityStatusEnumRegistry.cs` | 24 | Registry interface |
| `src/modules/workflow/.../Validation/IEntityStatusEnumContributor.cs` | 18 | Contributor marker interface |
| `src/modules/workflow/.../Validation/IWorkflowDefinitionEntityTypeLookup.cs` | 14 | Lookup interface for SaveGraph validator |
| `src/modules/workflow/.../Infrastructure/Validation/EntityStatusEnumRegistry.cs` | 50 | Singleton registry impl (FrozenDictionary) |
| `src/modules/workflow/.../Infrastructure/Validation/WorkflowDefinitionEntityTypeLookup.cs` | 30 | Dapper lookup impl |
| `src/modules/cases/.../Validation/CaseStatusEnumContributor.cs` | 17 | Case contributor |
| `src/modules/workflow/.../Commands/CreateWorkflowDefinition/CreateWorkflowDefinitionCommandValidator.cs` | 102 | Create validator |
| `src/tests/.../Workflow/WorkflowDefinitionValidationTests.cs` | 285 | Integration tests |

### Modified (5 files)
| File | Change |
|------|--------|
| `WorkflowInfrastructureModule.cs` | +DI: IEntityStatusEnumRegistry, IWorkflowDefinitionEntityTypeLookup |
| `InfrastructureRegistration.cs` (Cases) | +DI: IEntityStatusEnumContributor |
| `AqtCoreFW.Cases.Infrastructure.csproj` | +ProjectReference to Workflow.Application |
| `SaveDefinitionGraphCommandValidator.cs` | Extended with async state/transition validation |
| `AqtCoreFW.Workflow.Application.Tests.csproj` | +ProjectReference to Workflow.Infrastructure |

### Test Files Created (3 files)
| File | Tests |
|------|-------|
| `EntityStatusEnumRegistryTests.cs` | 9 tests |
| `CreateWorkflowDefinitionCommandValidatorTests.cs` | 8 tests |
| `SaveDefinitionGraphCommandValidatorTests.cs` | 6 tests |

## Test Results

| Project | Baseline | Current | Status |
|---------|----------|---------|--------|
| Workflow.Application.Tests | 410 | 434 | +24 new, all pass |
| Workflow.Infrastructure.Tests | 67 | 67 | unchanged, all pass |
| Integration (validation subset) | 0 | 3 | all pass |

### New Unit Tests (Step 8)
1. `Validate_HappyPath_ValidEntityTypeAndMatchingStateNames_IsValid`
2. `Validate_UnknownStateName_ReturnsErrorWithAllowedList`
3. `Validate_UnknownTransitionFromStateName_ReturnsError`
4. `Validate_UnknownTransitionToStateName_ReturnsError`
5. `Validate_CaseMismatch_LowercaseStateName_ReturnsError`
6. `Validate_UnknownEntityType_ReturnsErrorWithKnownTypes`
7. `Validate_EmptyEntityType_ReturnsError`
8. `Validate_MultipleInvalidStates_ReturnsAllErrors`

### New Integration Tests (Step 9)
1. `CreateWorkflowDefinition_ValidEntityTypeAndStateNames_ReturnsSuccess`
2. `CreateWorkflowDefinition_InvalidStateName_Returns422WithAllowedList`
3. `SaveDefinitionGraph_InvalidTransitionToStateName_Returns422`

## Error Payload Sample (D7 structured format)

```json
{
  "field": "States[0].Name",
  "errorMessage": "Invalid state name 'Bogus' for entity type 'Case'. Allowed: [Draft, Submitted, UnderReview, ReturnedForRevision, Approved, Rejected, Closed]"
}
```

## Build Status
- Type check: PASS (0W/0E)
- Unit tests: PASS (434 + 67)
- Integration tests: PASS (3/3)

## Handoff Notes for Group C (RBAC + Audit)

1. **Validators auto-registered** — FluentValidation assembly scan in `WorkflowApplicationModule.AddValidatorsFromAssembly()` picks up both validators.

2. **Structured error surface** — Validation errors flow through standard MediatR validation pipeline → API returns 400/422 with `errors[]` array containing `propertyName` + `errorMessage`.

3. **DI registrations added:**
   - `IEntityStatusEnumRegistry` (singleton) in WorkflowInfrastructureModule
   - `IWorkflowDefinitionEntityTypeLookup` (scoped) in WorkflowInfrastructureModule
   - `IEntityStatusEnumContributor` (singleton) in Cases InfrastructureRegistration

4. **SaveGraph validator is async** — uses `CustomAsync` to look up EntityType from DB before validating states/transitions.

5. **Case-sensitive validation enforced** — `StringComparer.Ordinal` used throughout.

## Concerns/Blockers
None — all acceptance criteria met.
