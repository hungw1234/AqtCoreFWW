# Dead-Code Removal: ISignatureOrchestrator.VerifySignerBindingAsync — Scope Trap

**Date**: 2026-07-04 10:41
**Severity**: Medium
**Component**: Core/Signing/Orchestration
**Status**: Resolved

## What Happened

Removed dead interface method `ISignatureOrchestrator.VerifySignerBindingAsync` and its implementation. Initial grep (src-only) reported 0 callers. Build failed on 9 constructor call sites in `SignatureOrchestratorTests.cs` that passed `signerBindingGuard` positionally to the now-removed parameter. All test sites constructed the orchestrator to test OTHER methods — never invoked the dead code itself.

## The Brutal Truth

The removal looked clean until the build. Initial grep gave false confidence because it searched `src/` only (`grep -rn ... src`), completely missing `tests/unit/`. This is insidious: the dead-code removal is **actually** safe (no callers anywhere), but the scope error made us look careless when the CI choked on test constructors. The real shame is that this is a pattern we've hit before — yet we still default to src-only scans.

## Technical Details

**Dead code confirmed via repo-wide grep:**
```
Interface: ISignatureOrchestrator.VerifySignerBindingAsync
Implementation: SignatureOrchestratorImpl.VerifySignerBindingAsync
Zero external callers (verified with `-rn` across entire repo post-fix)
```

**Build error footprint:**
```
SignatureOrchestratorTests.cs (9 ctor sites):
  new SignatureOrchestrator(
    strategy,
    signerBindingGuard,  // ← positional arg, now param doesn't exist
    ...
  )
```

**Collateral cleanup:**
- Removed orphaned `using Domain.Services` (IDE0005 tolerance but orphaned after interface deletion)
- Removed orphaned private `LoadAsync` method (only invoked by the dead `VerifySignerBindingAsync`)
- Removed unused `ISignerBindingGuard` primary-ctor param in impl

## What We Tried

1. Grepped `src/` only → reported 0 callers ✗
2. Deleted method + interface ✗
3. Built → 9 test constructor failures ✓ (caught it)
4. Re-grepped repo-wide (including tests/) → still 0 actual callers ✓
5. Updated 9 constructor call sites in tests ✓
6. Re-ran SignatureOrchestratorTests (11/11 pass) ✓

## Root Cause Analysis

**Primary:** Dead-code sweep scoped to `src/` gives false confidence. Deleted symbols must be verified against the **entire codebase**, including `tests/`, `Presentation/`, and any other project referencing the interface.

**Secondary:** No pre-deletion checklist enforced this. We knew the pattern (had hit it before) but defaulted to the fast path anyway.

## Lessons Learned

1. **Symbol deletion = repo-wide grep, not src-only.** When removing public contracts, test projects are FIRST-ORDER consumers. Missing them is not a "oops, minor edit" — it's a scope gap that breaks the build.

2. **Build is the backstop, not the plan.** Caught 9 issues that a src-only grep hid. Warnaserror + compile-time enforcement prevented shipping broken tests.

3. **Orphan markers are easy to miss.** Removing a method that ONLY one other (also-dead) method calls doesn't show up in grep — must manually trace call graphs or rely on IDE dead-code inspection.

## Next Steps

- ✓ Commit pending (9 test ctor sites fixed, build 0W/0E, SignatureOrchestratorTests 11/11, Architecture 149/149)
- Reinforce dead-code policy: ALWAYS grep `-rn pattern repo-root` (not `src/`), then add test-site inventory to commit message
- Consider IDE red-team on unused symbols pre-merge to catch orphans like `LoadAsync`

---

**Build Gate**: 0 Warnings, 0 Errors  
**Test Gate**: SignatureOrchestratorTests 11/11 ✓ | Architecture 149/149 ✓  
**Code Review**: 0 blockers
