# Process Handler Resolution P2b: ParallelBranch Stage-Only Persistence

**Date**: 2026-05-19 11:41
**Severity**: Medium
**Component**: WorkflowEngine / ParallelBranchService / Application-Infrastructure boundary
**Status**: Complete
**Commit**: bb73ab12 on `feat/process-handler-resolution-p2b` (stacked, not pushed)
**Version**: v2.137p72

## What Happened

Phase 2b isolated the stage-only prep for `InitiateBranchingAsync` — making it safe to call before a transaction commits, without changing pre-P3 behavior. The goal: enable P3's indivisible single-transaction model (AuthZ + RPC subsume + execute integration all atomic) by decoupling branch creation from persistence.

Discovered an architectural constraint: Application layer cannot reference WorkflowDbContext. This forced the design away from the "obvious" return-shape change (return staged entities for caller to persist) toward a new interface member: `IParallelBranchService.PersistStagedBranchAsync(ct)`.

Sole production caller verified by grep: `ExecuteTransitionCommandHandler`. Calls persist on the next line inside the RETAINED swallow-all catch block — byte-identical pre-P3 surface, zero ripple on NSubstitute consumers (additive member, same pattern as P2's IIdentityModuleClient).

## The Brutal Truth

The "obvious" fix was wrong. Returning staged entities would have leaked DbContext references into Application — violating the boundary that P2 already established. Would've caught this at P3 merge when the AuthZ mega-PR forced a rearchitect. Better to honor constraints now than paint yourself into an architectural corner.

Also maddening: a test assertion passed green while masked by a tenant query filter. Fresh-context invariant was obscured because the seeder used random tenantId while the fresh DbContext had TenantId=Guid.Empty. Would've shipped a latent false-green — the kind that bites during a real multi-tenant scenario.

## Technical Details

**Design Shape B** (realized):
```csharp
// New interface member
Task IParallelBranchService.PersistStagedBranchAsync(CancellationToken ct)
  => _dbContext.SaveChangesAsync(ct);

// Caller (ExecuteTransitionCommandHandler) — unchanged pattern
var staged = await _parallelBranchService.InitiateBranchingAsync(...);
await _parallelBranchService.PersistStagedBranchAsync(ct);  // Added this line, inside catch
```

No return-shape change. InitiateBranchingAsync still returns void (staging is a side-effect on the DbContext's tracked entities). Consumer handlers remain unaware of staging semantics — they just call the service as before, now with an explicit persist step.

**Test Harness Bug Fixed**:
```csharp
// BEFORE (masked by filter)
var fresh = new WorkflowDbContext(optionsBuilder.Options);
var staged = fresh.ParallelBranches.ToList();  // Filter=true, so if TenantId≠seeded, result is empty
Assert.Empty(staged);  // GREEN, but FALSE

// AFTER (IgnoreQueryFilters on both)
var staged = fresh.ParallelBranches.IgnoreQueryFilters().ToList();
Assert.Empty(staged);  // GREEN, TRUE (no silent data hiding)
```

Hardened both stage-only and persist assertions with explicit filter bypass.

**New Coverage Added**:
- ParallelBranchServiceTests: 6/6 new facts (AND/OR/N-of-M CheckJoinCondition join paths)
- CheckJoinCondition now has direct regression suite (was previously zero coverage on join logic)
- Workflow.Infrastructure.Tests: 64/64 steady
- Consumer handlers: ExecuteTransition/ResolveBranchChild/CompleteTask/CancelTask: 40/40 steady
- Architecture.Tests: 101/101 steady
- Build: 0W/0E

**Pre-Existing Failures** (not regressions):
- 23 Application failures: Create/Clone/Update WorkflowDefinition `FromStateId must be persisted` bug-class
- Worktree-proven IDENTICAL on parent feat/p2 branch → not introduced by P2b
- Inherited defect from earlier phase, separate from stage-only work

## What We Tried

1. **Return-shape approach** (rejected): Return staged entities from InitiateBranchingAsync, let Application caller persist. Violated Architecture boundary (Application→Infrastructure). Blocked at design review.

2. **Wrapper-closure approach** (explored): Wrap both stage + persist into a single `InitiateBranchingAndPersistAsync` method. Violated P3's design constraint (need stage-only for pre-transaction authz checks). Discarded.

3. **Interface method approach** (adopted): Add `PersistStagedBranchAsync` as separate concern. No return-shape change, additive member, preserves boundary. Realized Shape B. ✓

## Root Cause Analysis

**Architectural Constraint Was Load-Bearing**: The rule "Application does not reference WorkflowDbContext" wasn't aspirational — it drove every design choice in P2. Ignoring it in P2b would have created a landmine for P3 (AuthZ subsume requires strict Application purity).

**Test Filter Masking Was a Pattern Vulnerability**: TenantId=Guid.Empty in fresh DbContext while seeded data uses random tenantId → query filter silently excludes all rows. Assertion reads empty list as "correct staging behavior" when in fact staging never ran. Same false-green lesson from Phase-04a (IHostedService stripping).

The fix: **Always IgnoreQueryFilters on assertions that verify side-effects against a fresh context**. Filters are for production safety, not for test truth.

## Lessons Learned

1. **Constraints Shape Interface Design**: An "obvious" shape change that violates a boundary isn't obvious — it's wrong. Design to constraints, not to convenience.

2. **Grep Before Trusting "Only One Caller"**: Verified the claim `ExecuteTransitionCommandHandler` is the sole prod caller. Would have discovered unanticipated consumers if grep lied. Don't trust memory — verify.

3. **Fresh-Context Tests Need Filter Bypass**: When testing "did this operation stage correctly?", you cannot use a fresh DbContext with active filters. The filter may silently exclude the very data you're asserting about. IgnoreQueryFilters is not optional.

4. **Phase Gating Discipline Prevents Scope Creep**: P2b was scoped explicitly ("stage-only, gate before P3"). Mid-session advisor question about per-phase gating pushed back: "this is a design decision, belongs to P3 RPC subsume discussion, not P2 staging." Kept the boundary tight. Prevented 2-day scope spiral.

## Next Steps

**P3 (RPC Subsume + AuthZ Gate + Execute Integration)**: Single indivisible PR. Requires explicit user confirmation before start — phase gating rule applied.

**P3 Will**:
- Drop the interim `PersistStagedBranchAsync` call (fold staged entities into single outer transaction)
- Remove swallow-all catch (all-or-nothing behavior belongs with transaction atomicity, not error suppression)
- Consume InitiateBranchingAsync return (will shift back to return-shape, now with correct AuthZ context in place)

**Timeline**: User approval gate before P3 starts. Not resuming session until explicit direction.

---

**File**: C:\AqtCoreFW\docs\journals\journal-260519-1141-process-handler-resolution-p2b-parallelbranch-stage-only.md

