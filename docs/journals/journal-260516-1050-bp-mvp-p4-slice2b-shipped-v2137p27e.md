# BP MVP P4 Slice 2B — GetIntakeFormSchema cross-module

**Date**: 2026-05-16 10:50
**Severity**: Low
**Component**: BusinessProcess.Application + Forms.Contracts integration
**Status**: Resolved

## What Happened

Slice 2B shipped ~50min (09:15 → 10:50 cumulative today after Slice 2A-Read + 2A-Write). Scope collapse from inherited carry-over: memory listed "IBusinessProcessQueryClient (5 methods) + GetIntakeFormSchema, ~1.3d, unblocks P6/P7/P8" but pre-cook verification revealed zero consumers for the 5-method contract across entire codebase. P6 plan doesn't call it; P7/P8 are FE-only. Result: shipped only `GetIntakeFormSchema` handler + minimal `IFormTemplateQueryClient` (1 method) from Forms.Contracts, eliminating ~1d of dead-code work.

**Ship #3 of code today:** v2.137p27e (`6f8b559a` feat + `20d825ab` docs) on origin/main. Tests 28/28+19/19+2/2+155/155+97/97+10/10 PASS, build 0E/0W, review 9.4/10.

## The Brutal Truth

This is **exactly** why inherited task labels drift. Memory carry-over from session boundary reads "next Slice 2B" → plan says "5 methods" → we design + code + test + ship all 5 → 1.25d blown on a feature **nobody uses**. The trap is insidious: it's not wrong per se, just obsolete. P6 was designed concurrently; its actual API needs didn't match the pre-design contract sketch from planning phase.

What saves us here: advisor-first reconcile call took ~5min of grep + plan reread. Reconcile is **cheaper than re-implementation**. But only because we checked before opening scope.

## Technical Details

**Contract:**
```csharp
// Forms.Contracts — 1 method (was: 5 planned)
public interface IFormTemplateQueryClient
{
    Task<BusinessProcessIntakeFormDto?> GetVersionSchemaByInternalIdAsync(
        long tenantId, int versionNumber, Guid publicId, CancellationToken ct);
}

// BP.Application — in-process impl
internal sealed class InProcessFormTemplateQueryClient : IFormTemplateQueryClient
{
    // Dapper: SELECT schema FROM [forms].[FormTemplateVersion]
    // JOIN [forms].[FormTemplate] ON both sides guard TenantId (defense-in-depth RLS)
}
```

**Handler:**
```csharp
public sealed class GetIntakeFormSchemaQueryHandler 
    : IQueryHandler<GetIntakeFormSchemaQuery, BusinessProcessIntakeFormDto?>
{
    // Pattern-A guard: pre-DB tenant validation
    // Delegate to Forms client (not raw SQL — cross-schema raw banned per arch tests)
    // Return NotFound if snapshot missing (soft-deleted template)
}
```

**Controller:**
```csharp
[HttpGet("{id}/intake-form")]
[Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
public async Task<IActionResult> GetIntakeFormSchema(long id)
{
    var result = await _mediator.Send(new GetIntakeFormSchemaQuery(id));
    return result.IsSuccess && result.Value is null 
        ? NoContent()  // Explicit 204 when no form pinned
        : ToApiResponse(result);  // 200 + body or error
}
```

**Tests:**
- 6 unit facts NSubstitute over `IFormTemplateQueryClient`
- 1 nested helper `GetIntakeFormSchemaRow` (was private → namespace scope for NSubstitute generic binding; refactored mid-implementation when test binding failed)
- Pattern: for Dapper handler tests, expose Row type at namespace scope if handler needs generic materialization binding

**Build:** Solution-wide 0E/0W. Tests +1 file `GetIntakeFormSchemaQueryTests.cs` (28/28 handler total now).

## What We Tried

1. **Pre-cook scope verification** (5 min): grep `IBusinessProcessQueryClient` across all modules → zero matches. Grep carry-over feature names (DeleteFormTemplate, DeactivateDefinition) → no handlers. Reread P6/P7/P8 plans → P6 uses `IFormSubmissionRunClient`; P7/P8 consume controller endpoints only.

2. **Decision point**: scope trim vs. schedule slip. Advisor reconcile faster than re-impl → collapse to GetIntakeFormSchema only.

3. **Test binding fix**: initial nested class `private sealed class IntakeRow` failed NSubstitute generic bind. Pulled to `GetIntakeFormSchemaRow` at namespace scope. Trade-off: slight namespace pollution vs. clean test access (chosen).

4. **204 branching**: shared `ToApiResponse<T>` helper returns 200+null-body on success-with-null. REST correct shape = 204 No Content. Explicit check in controller; comment documents the gap.

## Root Cause Analysis

**Scope drift at session boundary.** Slice 2A (Read/Write) was vertical-axis ship (2 days compressed to 0.4 + 0.4 actual). Slice 2B carry-over was written as placeholder ("next is IBusinessProcessQueryClient...") without re-checking whether those 5 methods had live consumers in current plan. Sessions compress time; hand-off context degrades. Memory notes what was *planned*, not what's *needed*.

**Why it didn't surface earlier**: The 5-method contract fit the *architecture narrative* (cross-module client pattern), so it felt safe to ship. Only ground-truth is: grep the codebase + re-verify plan. Codify this as part of carry-over acceptance.

## Lessons Learned

1. **Verify consumer existence before inherited scopes.** Carry-over task labels are directional, not truth. Codify: grep for callers + reread consumer plans before opening scope. Takes ~5min, saves ~1d. Non-negotiable for "contract/client" tasks.

2. **Test private-type refactor pattern for Dapper handlers.** When NSubstitute can't bind `QueryFirstOrDefaultAsync<TPrivateRow>`, expose the Row type at namespace scope (slight pollution) rather than marking handler class public. Cleaner than alternatives (a) make nested class public, (b) suppress generic binding).

3. **Explicit 204 branching in API responses.** Shared `ToApiResponse<T>` helper is not a one-size-fits-all. When success-with-null-value should map to 204 (not 200+body), add explicit guard in action + comment.

4. **Snapshot missing = NotFound, not 500.** Orphaned FK (template soft-deleted post-BP creation, pre-R5 cascade guard exists) is a data-integrity issue, but from the caller's perspective it's "intake form doesn't exist" (no enumeration oracle). Logged warning for ops; HTTP 404.

5. **Ship-#3-of-day band sustainable.** Three ships 09:15 → 10:30 → 10:50 post-5-override fatigue. Cadence holding; no compounding risk.

## Next Steps

**Carry-over Slice 2C (~0.3-0.5d Medium):**
- 3 runtime endpoints (Start Run, Execute Transition, Submit State Form)
- 3 BP repo methods (ResolveWorkflowDefinitionInternalId, GetStateFormTemplateIds, GetTransitionFormTemplateIds)
- TransactionScope coordination
- Integration tenant-isolation tests
- dependency-map.md finalize

**Future-use follow-ups (blocked on R5/R6):**
- P4-2B-FU1: IsFormTemplateReferencedAsync when R5 DeleteFormTemplate guard is built
- P4-2B-FU2: ListActiveBpsUsingWorkflowAsync when R6 DeactivateDefinition guard is built

**Open FU families:**
- P3-FU1 (~3-4h): TenantIsolation enforcement test
- P4-2A-Read-FU, P4-2A-Write-FU (still filed)

**Owner**: Next cook session. Journal untracked at close; bundle on next docs commit per established pattern.

---

**Status:** DONE
**Summary:** Slice 2B shipped after advisor-first scope collapse (5-method dead-code contract → 1-method needed schema client). 50min wallclock; carry-over verification + consumer check pattern codified for future cross-module tasks.
**Concerns:** None — scope collapse is the right call; memory drift is expected across session boundaries. Reconcile check < re-impl.
