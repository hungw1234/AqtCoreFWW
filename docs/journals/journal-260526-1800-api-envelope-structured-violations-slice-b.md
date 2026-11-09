# Journal: T3-API-ENVELOPE-STRUCTURED-VIOLATIONS Slice B — ApiError Metadata Envelope

**Date:** 2026-05-26 ~18:00 ICT  
**Version:** v2.137p117 (working tree)  
**Author:** orchestrator

## Summary

Shipped Slice B of T3-API-ENVELOPE-STRUCTURED-VIOLATIONS: structured domain-error metadata now surfaces through the API error envelope via a DEFAULT-DENY OPT-IN mechanism. Blast radius: SharedKernel `ApiError` + `ApiResponse` (every endpoint's error shape), but additive + backward-compatible.

## What Shipped

**SharedKernel (2 files)**

`ApiError.cs`: Added optional `IReadOnlyDictionary<string, object?>? Metadata = null` as 6th positional parameter. JSON attribute `[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` prevents `"metadata":null` appearing in error responses. Required because `Program.cs` `AddJsonOptions` sets no global `WhenWritingNull`.

`ApiResponse.cs` `Fail()`: Extracts only the reserved `"PublicMetadata"` key from `IError.Metadata` into `ApiError.Metadata`. All other FluentResults keys (`ErrorCode`, `GraphErrors`, `WorkflowErrorCode`, etc.) stripped at this boundary. Comment documents the `IReadOnlyDictionary<TKey,TValue>` invariant-TValue runtime trap.

**Workflow.Domain (1 file)**

`WorkflowErrors.GraphValidationFailed`: Added two metadata keys:
- `"GraphErrors"`: raw `IReadOnlyList<GraphRuleViolation>` — internal/debug only, never reaches HTTP response.
- `"PublicMetadata"`: wire-safe projection `{ violations: [ { Rule, StateName, TransitionActionName, Detail, Metadata } ] }`. Producer uses explicit `(IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>{...}` cast — without this cast, the `is IReadOnlyDictionary<string, object?>` pattern match in `Fail()` fails at runtime (TValue invariance: `object` ≠ `object?`).

Error message updated: "See metadata.violations for per-rule details."

**Unit tests (1 file)**

`WorkflowGraphValidatorTests.cs` V11 extended:
- `PublicMetadata` key presence assertion
- Type assertion: `BeAssignableTo<IReadOnlyDictionary<string, object?>>()` — regression guard for the invariant-TValue trap
- Violations list shape assertions

**Integration tests (1 file)**

`WorkflowByGroupProducerAndSaveValidationTests.cs`:
- NEW `ExtractPublicViolationsAsync` helper: navigates `errors[0].metadata.violations[*]` from camelCase JSON
- I4 (`I4_Save_ByGroupUnknownOnly_ReturnsUnknownInMetadata`): full wire assertion — `unknownCodes` contains `"DOES_NOT_EXIST"`, `JsonElement` cast for deserialized values
- I5 (`I5_Save_ByGroupMixedKnownAndUnknown_ReturnsOnlyUnknownInMetadata`): self-contained seed (`SeedActiveGroupAsync("GROUP_TEST_A")` added at start — fixes xUnit non-ordering dependency on I1); partition assertions: TYPO_X + TYPO_Y present, GROUP_TEST_A absent

**FE (1 file — schema only)**

`aqtCoreFWAPIV1.schemas.ts`: manually added `ApiErrorMetadata` type + `metadata?` field on `ApiError` interface. Pending next orval regen to become authoritative.

FE STOP gate applied: `normalizeError` in `api-error.ts` strips all fields to fixed `NormalizedApiError` shape; `handleSave` generic toast. No existing structured-violation display pattern. Rendering requires new UX design (modal vs inline vs toast expansion) — deferred per spec.

## Key Engineering Decisions

**D1: DEFAULT-DENY OPT-IN.** Only the reserved `"PublicMetadata"` key routes to wire. Prevents accidental exposure of internal debug data (stack traces, raw entity lists) by any future developer adding FluentResults metadata keys without understanding the HTTP surface.

**D2: Invariant TValue cast.** `IReadOnlyDictionary<TKey,TValue>` is invariant in `TValue`. `Dictionary<string, object>` does NOT satisfy `is IReadOnlyDictionary<string, object?>` at runtime. Producer must use `(IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>{...}`. V11 type assertion tests this invariant on every CI run.

**D3: `[property: JsonIgnore]` over global config.** `Program.cs` `AddJsonOptions` only adds `JsonStringEnumConverter`; no `DefaultIgnoreCondition = WhenWritingNull`. Positional-record parameter attribute with `property:` prefix is the correct syntax.

**D4: I5 xUnit ordering fix.** xUnit does not guarantee test execution order within a class. I5 required `GROUP_TEST_A` seeded by I1. Fixed by embedding `SeedActiveGroupAsync("GROUP_TEST_A")` in I5 directly — test is now self-contained regardless of execution order.

## Wire Shape

```
response.errors[0].metadata.violations[*]
  .rule           — rule identifier string
  .stateName      — nullable
  .transitionActionName — nullable
  .detail         — human description
  .metadata       — rule-specific map (e.g. { "UnknownGroupCodes": ["TYPO_X","TYPO_Y"] })
```

Note: deserialized `metadata` values arrive as `JsonElement` at runtime (not boxed arrays). Consumers must call `.EnumerateArray()` or `.GetString()`.

## Build / Test Status

- SharedKernel build: 0E/0W
- Workflow.Domain build: 0E/0W
- WF.App unit tests: 585/585
- WF.Integration I4+I5: assertions verified against live API
- Pre-existing CS4014 errors in Release config (`TreatWarningsAsErrors`) are unrelated to this slice — present before stash, confirmed by git stash test

## Deferred

- Orval regen: `aqtCoreFWAPIV1.schemas.ts` change is manual until next `npm run generate-api` run.
- FE UX: rendering structured violations requires new design decision (not part of this slice per spec).
