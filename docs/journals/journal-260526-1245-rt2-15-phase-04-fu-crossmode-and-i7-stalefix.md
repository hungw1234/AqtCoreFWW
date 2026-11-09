# RT2-15 Phase 04 FU — Cross-Mode Pool Test + I7 RED Main Regression Fix

**Date**: 2026-05-26 12:45
**Severity**: High (regression on main masked by filtered test run)
**Component**: Workflow.Integration tests, clause-mode support
**Status**: Resolved

## What Happened

Planned 1h test-only follow-up for Phase 04 ByOrgUnit (commit 68287c52, v2.137p114 shipped earlier today). Goal: add cross-mode candidate-index non-interference integration test per Phase 03-FU pattern to lock ByOrgUnit+ByRole+ByGroup OR-arm safety.

Delivered 2-fact cross-mode test (first-try GREEN). But during full-assembly tester sweep, uncovered **1 RED test on main: `I7_Activate_WithNotSupportedClauseMode_Returns422` expecting 422, getting 204.**

Root cause: Phase 04 ship made ByOrgUnit a supported clause mode, rendering `_notSupportedModes` empty. I7's 422 expectation became stale. Main has been RED since 68287c52 — the Phase 04 tester ran filtered-only and never executed I7.

## The Brutal Truth

We shipped a test-only Phase 04 ("GREEN") on a filtered test run that masked a legitimate regression on main. The full assembly was RED for 2+ hours and nobody noticed because tester scope was scoped narrowly. This is process, not engineering — but it burns trust in "GREEN" claims. The filtered run was well-intentioned (time-box tight scope), but it became permission-by-default to skip the full picture.

## Technical Details

**Test on main before fix:**
```
I7_Activate_WithNotSupportedClauseMode_Returns422: 
  Expect: 422 Unprocessable Entity
  Actual: 204 No Content (because ClauseMode.OrgUnit now in supported set)
```

**Fix:** Inverted I7 to lock positive ByOrgUnit activate path (rename to `I7_Activate_WithOrgUnitClauseMode_Returns204_AndPersistsActive`). This ADDED real coverage — no prior unit-level test of ByOrgUnit activation existed, so the flip wasn't just un-REDing, it was closing a gap.

**Tests post-fix:** WF.Integration 62/66 (4 skip, clean). WF.Application 577/577. Full build 0E/0W. Code-review APPROVE-WITH-NITS 9.1/10 (1 Medium: type-discriminator validation anti-tautology filed as follow-up; 1 Low fixed pre-merge: stale duplicate XML-doc).

## What We Tried

1. Audit-at-slice-start (INVARIANT 260522-A) caught backlog row drift: row claimed "mirrors Phase 03-FU pattern" but no such test existed. Real mirror: single-mode `WorkflowTaskRoleClaimIntegrationTests`. Scoped new test to pure read-side OR-arm, skipped temporal re-seeding (already locked by `OrgUnitModuleClientTemporalTests`).
2. New cross-mode test passed immediately.
3. Ran full assembly on tester recommendation (not filtered). Red flag appeared.

## Root Cause Analysis

Two-part failure:

1. **Phase 04 tester ran filtered set** — intentional scope-boxing for time. But filtered != validated; tester reported "DONE-GREEN" without noting the filter caveat. Orchestrator accepted claim without challenging scope.
2. **No full-assembly gate on clause-mode slices** — cross-cutting changes (new supported clause) ripple across stale assertions in seemingly-unrelated tests. `_notSupportedModes` is a implicit contract; changes to it require full assembly validation, not filtered runs.

This is **2nd reinforcement of INVARIANT 260525-C** (tester accommodation pattern). 1st: Phase 02 deferred-to-next-phase claim that tested nothing. 2nd: Phase 04 filtered-green that left main RED. Pattern: scope claims decay 60-80% on full assembly. Tester prompts on cross-cutting concerns (enum fields, config constants, supported-set expansions) need explicit "full assembly, no filters" instruction + pre-merge verification.

## Lessons Learned

1. **Filtered test runs are not a proxy for full validation** — document the filter in the tester prompt, require explicit full-assembly sweep for cross-cutting slices (new clause modes, new perm codes, enum changes).
2. **Stale fixture assertions are invisible until executed** — when a slice changes configuration state (supported clause modes), pre-impl grep for stale expectations is cheaper than post-ship red-flagging. Add to code-review checklist: "Clause-mode additions → grep `NotSupported` + `422` in same file?"
3. **Inverted assertions add value, not just fix redness** — I7 flip closed a real gap (zero activation coverage for ByOrgUnit). Future: ask "does this test fix unlock new coverage?" to frame the rework constructively.

## Next Steps

1. **Filed follow-up backlog rows:**
   - `RT2-15-PHASE-04-FU-M1`: type-discriminator validation anti-tautology (medium, ≤1h)
   - `WF-TESTER-FULL-ASSEMBLY-GATE`: orchestrator/tester process INVARIANT (promote to rules/code-standards.md once validated; affects all future cross-cutting slices)
2. **Merge commitment:** Commit `188fbd35` pushed to main. Working tree clean. Code-review post-merge approved.
3. **Memory anchor:** INVARIANT 260525-C reinforced 2nd instance. Update project MEMORY.md to surface full-assembly gate as hard requirement for clause-mode / config / enum-state changes.
