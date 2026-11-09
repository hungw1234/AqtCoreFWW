# Forms cascade M1 Phase-06 BE - in-row TableField cascade membership guard

**Date:** 2026-08-12 17:20 ICT
**Branch:** `feat/forms-cascade-public-external-inrow`
**Slice:** M1 Phase-06 BE only (Phase-07 FE deferred to next session per user)
**Related plan:** `plans/260805-1409-forms-cascade-public-external-inrow/phase-06-*.md` (local, gitignored)

## Executive summary

Shipped the server-side in-row cascade membership guard for TableField child columns. Enum/Internal
child values not in the server-resolved option-set → 422 hard-reject; External soft-log (per
Phase-06 DoS-surface decision). Backward-safe via optional `Guid? tenantId=null` on
`IFormValidationService.ValidateAsync` - legacy 2-arg callers (6 test files) compile unchanged and
skip the guard. Extracted guard into a dedicated `InRowCascadeMembershipValidator` (~200 LOC) to
keep `FormValidationService` under the 200-LOC modularization threshold.

654 Forms unit tests green (200 Domain + 384 Application + 70 Infrastructure). Code review caught
one blocker-adjacent (non-Ref field type with spurious `dependencies:[]` → 500 crash bypass);
fixed in-slice with a defense-in-depth `FormFieldType` gate at guard entry.

## What shipped

### Interface (backward-safe contract change)

`IFormValidationService.ValidateAsync` gains `Guid? tenantId = null` between `data` and
`cancellationToken`. Default null keeps every legacy `.new()`+`.ValidateAsync(fields, data)` call
compiling and skipping the guard (fail-open on null tenant OR null resolver factory - both
dimensions must be present to activate).

### FormValidationService

Two ctors: parameterless (legacy callers / unit tests) + DI-injected
`(IDataSourceResolverFactory?, ILogger<FormValidationService>?)`. Runs the guard as a final step
after the existing per-field validation loop when both `_cascadeGuard is not null` AND
`tenantId is Guid tid`.

### InRowCascadeMembershipValidator (new, extracted)

- Iterates every TableField in `activeFields`; groups sibling child columns by `ParentFieldId`.
- For each child with a non-empty `dependencies:[]` in `DataSourceJson`, iterates rows from
  `FormDataPreprocessor.ParseTableFieldRows` (visibility bumped `internal → public` for
  cross-assembly reuse - only that one method needs it).
- Extracts parent values from the same row (cascade is row-scoped in v1: parent must be a sibling
  column) into an ordinal-keyed dict keyed by `filterParamName`.
- Dispatches to `IDataSourceResolverFactory.Create(child.Type).ResolveAsync(dsJson, tenantId, parentValues, ct)`.
- Membership check: child value must equal one of the returned options (`StringComparison.Ordinal`).
- Rejection taxonomy: Enum/Internal → `ValidationError` with message containing "cascade" (422);
  External → `LogWarning` only, submit passes.
- Exception filter `when (isExternal)` swallows resolver crashes only on the External path -
  Enum/Internal failures still surface.

### Handler wiring

- `SubmitFormCommandHandler:59` passes `request.TenantId` (authed path has ambient tenant).
- `SubmitPublicFormCommandHandler:79` passes `row.TenantId` from the RLS-bypass Dapper lookup
  (anonymous public caller has no ambient tenant).

### Test coverage

New `InRowCascadeMembershipValidationTests` (10 tests, all green):

1. Enum child value in resolved set → no error
2. Enum child value not in set → 422 with "cascade" in message
3. Enum multi-row, each row resolved independently (call-count assertion)
4. External resolver throws → soft-log, submit passes
5. Child without `dependencies:[]` → guard skipped (backward flat option-set)
6. `tenantId = null` → guard skipped (legacy caller safety)
7. No TableField in fields → guard early-return (factory.Create never called)
8. JsonElement array rows accepted (System.Text.Json binding path)
9. **[C-1 fix]** Non-Ref child type with spurious `dependencies:[]` → guard skips, no crash
10. Multi-parent dependency → both parent values packed into resolver call

### Backlog rows filed (from code review)

- **`FORMS-CASCADE-INROW-INTERNAL-PARENT-FILTER`** T3 `[v1.1]` (H-1): InternalDataSourceResolver
  ignores `parentValues` (deferred). Guard's Internal path only catches value ∉ full domain, not
  narrower cross-parent tampering. Blocked-on: real Internal-cascade consumer example.

- **`FORMS-CASCADE-INROW-GUARD-N-PLUS-1-DEDUP`** T3 `[v1.1]` (H-2): O(N-rows × M-children)
  sequential resolver calls, no dedup. 500 rows × 3 cascade children = 1500 sequential MediatR
  queries. Fix: group by `(child.Id, parentValues-tuple)`, resolve once per distinct combo.

## What the review caught (and how)

C-1 verification pattern was the payoff: reviewer traced `DataSourceResolverFactory.Create` (line
19-20) and found it throws `ArgumentException` for non-Ref field types. Publish-time
`CascadeDependencyValidator.ValidateSubGraph` does NOT gate on `FormFieldType` - it only checks
graph shape. So a hand-crafted `DataSourceJson` with `dependencies:[]` on a Text/Date child would
reach the guard and crash the entire submit (500, user data lost).

Fix: 5-line `FormFieldType` allowlist gate at guard entry + 1 test with a Text child. Blast
radius: the ArgumentException path was never exercised in tests because publish-time UI
constraints happen to block it - a classic "obvious seam may never have worked" gap (see lesson
`feedback-obvious-seam-may-never-have-worked`). Publish validator will get the same gate as
proper fix (blocks admin misconfig upstream), tracked separately.

## Change-Impact Symmetry (4 axes)

1. **Backward.** `IFormValidationService.ValidateAsync` gains OPTIONAL `Guid? tenantId = null`.
   6 legacy test callers with parameterless `new()` + 2-arg `.ValidateAsync(fields, data)`
   compile unchanged; guard skips on null tenant OR null factory. Publish path (cascade v1
   shipped 2026-08-05) untouched.

2. **Downstream.** 7 mock `.ValidateAsync(...)` sites in 4 Application test files updated
   in-slice (`Arg.Any<Guid?>()` inserted at position 3). 2 handler call sites
   (`SubmitFormCommandHandler:59` + `SubmitPublicFormCommandHandler:79`) updated to pass real
   `TenantId`. No downstream consumer of `IFormValidationService` outside these.

3. **Symmetry.** Interface signature ↔ FormValidationService impl ↔ 2 handler call sites ↔
   7 test mocks all synchronized. `FormDataPreprocessor` visibility `internal → public` is
   the mirror change for cross-assembly reuse of `ParseTableFieldRows` - not a fake shim.

4. **Bypass.** No `SELECT *` / `INSERT ... SELECT` in slice. Reviewer found ONE bypass surface:
   non-Ref FormFieldType at admission → guard entry. Fixed in-slice with defense-in-depth type
   gate; upstream fix at publish validator tracked separately.

## Test regression walk (execution paths, not nouns)

- **"Publish a template with in-row cascade"** → `FormTemplate.Publish()` →
  `CascadeDependencyValidator.Validate()` → per-TableField sub-graph Kahn topo-sort →
  22/22 cascade tests + 200/200 Domain regression pass.
- **"Authed user submits a form with a TableField cascade child"** → `SubmitFormCommand` →
  `SubmitFormCommandHandler.Handle` → `validationService.ValidateAsync(fields, data,
  request.TenantId, ct)` → guard iterates rows → resolver returns filtered set → membership
  assertion → 384/384 Application tests pass (7 mock sites updated for new signature).
- **"Anonymous public user submits a form with a TableField cascade child"** →
  `SubmitPublicFormCommand` → `SubmitPublicFormCommandHandler.Handle` → Dapper resolves
  `row.TenantId` via `QueryFirstOrDefaultWithPublicFormsBypassAsync` → `ValidateAsync(...,
  row.TenantId, ct)` → guard runs under public path → 68/68 Infrastructure regression + 10 new
  cascade tests pass.
- **"Legacy caller with parameterless FormValidationService"** →
  `AddressFieldValidationTests` / `RequiredIfJsonValidationTests` → `new FormValidationService()`
  + `.ValidateAsync(fields, data)` → guard = null → guard skipped → 68/68 pass unchanged.

## Deferred (out of slice)

- **Phase-07 FE:** per-row cascade wiring in `web/src/features/forms/builder/table-field-widget.tsx`
  (currently plain `useState<Record<string, unknown>[]>`, NOT antd Form.List; row-scoped watch
  pattern needed). Deferred to next session per user (BE-only today decision at cook start).
- **H1 (top-level External-authed on authed submit):** DEFERRED, `ResolveInternalDataSourceDefaultHandler`
  returns [] with zero real handler wired (vacuous today).
- **Publish-time FormFieldType gate:** filed as follow-up (defense-in-depth at guard entry is
  the safety net; belt-and-suspenders fix at publish validator is the proper block).

## Files changed (11)

**Modified:**
- `src/modules/forms/AqtCoreFW.Forms.Application/Services/IFormValidationService.cs` — +tenantId param
- `src/modules/forms/AqtCoreFW.Forms.Application/Commands/SubmitForm/FormDataPreprocessor.cs` — visibility bump
- `src/modules/forms/AqtCoreFW.Forms.Application/Commands/SubmitForm/SubmitFormCommandHandler.cs:59` — pass TenantId
- `src/modules/forms/AqtCoreFW.Forms.Application/Commands/SubmitPublicForm/SubmitPublicFormCommandHandler.cs:79` — pass row.TenantId
- `src/modules/forms/AqtCoreFW.Forms.Infrastructure/Services/FormValidationService.cs` — 2 ctors + async + guard invocation
- 4 test files under `tests/unit/AqtCoreFW.Forms.Application.Tests/Commands/` — 7 mock signature updates
- `backlogs/backlog.md` — M1 row status BE-done; 2 new rows H-1/H-2 filed

**New:**
- `src/modules/forms/AqtCoreFW.Forms.Infrastructure/Services/InRowCascadeMembershipValidator.cs`
- `tests/unit/AqtCoreFW.Forms.Infrastructure.Tests/Validation/InRowCascadeMembershipValidationTests.cs`

## Unresolved questions

- Should publish-time `CascadeDependencyValidator` gate `dependencies:[]` on non-Ref FormFieldTypes
  (admission fail-closed) in addition to the guard's defense-in-depth type filter? Recommend YES,
  file as follow-up after Phase-07 FE ships.
- Ship-slice ordering: merge C1+H2+M1 as one PR after Phase-07 FE lands, or ship BE now and FE
  later as 2 separate PRs? Current bias: single ship-slice after FE for tighter reviewability.
