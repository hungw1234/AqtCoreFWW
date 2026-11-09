# Process-Handler-Resolution P3 SESSION-5: Resolver Fail-CLOSED Authz Gate

**Date**: 2026-05-20 03:00  
**Severity**: Critical  
**Component**: Workflow.Application handlers, resolver authz layer, Model-B transition authorization  
**Status**: Resolved + Ready for S6 (STOP / FRESH-CONTEXT MANDATED)

## What Happened

SESSION-5 implemented the security-core fine-grained resolver gate replacing the removed RPC design (commit 508ed525, v2.137p76, local-only, 22 files / 20M3N, branch 12 ahead origin/main). Model-B resolver fail-CLOSED authz on 3 handlers (ExecuteTransition, GetAvailableTransitions, ResolveBranchChild continuation). Two-invocation design per spec line 28 + plan D1: authz-of-T over current-state's rule (403 on deny) + WF_NO_HANDLER over target non-final (422 GOV_WFL_014). New `IWorkflowDefinitionRepository.GetOutgoingWithAssignmentsAsync` (2-query + in-memory left-join, no-tracking). New `ResolverAuthz` shared helper. RPC column + DTO retained for Slice 8 M2 drop.

**Build**: 0E/0W. **Unit**: Workflow.Application 478/478, Workflow.Infrastructure 63/63. **G1**: 5/5 (coarse policy untouched). **G2**: Architecture 101/101. **G3**: Workflow.Integration 23p/1f/2skip (baseline-identical post-fix). **G4**: SchemaUniversalsTests 1/1. **New**: WorkflowFineResolverGateTests 4/4. **Code review**: 9/10, Crit 0, High 0.

## The Brutal Truth

Five defect corrections across five rounds. First impl conflated two resolver invocations—authorized actor against Pool(targetState) instead of T's rule over current state. Tests retrofitted to the wrong semantic → subagent reported 4/4 green. Read diff against spec caught it. Then fine gate skipped entirely for final-state targets (wrong guard placement). Then creator-recall regression (creator-guard fallthrough logic misread). Then M1 fail-OPEN on def-load transient failures + M2 InitiatorUserId column-name footgun + L1 bare catch with no observability. Then G3 baseline-shift: 21p/3f→23p/1f—tester claimed "pre-existing concurrency," but worktree-diff proved 2 NEW fails (no assignments seeded in fixtures). Worked fixtures to assignment-rule model instead of weakening the gate. All five classes caught by reads of diff + independent reasoning + worktree-proof discipline.

## Technical Details

**Fine-gate shape (per spec line 28):**
```
if (!transition.IsRecall) {
  Resolve(current, outgoing(current), context) → AuthorizePerRule
  if (!targetState.IsFinal && outgoing(current).Empty) → 422 WF_NO_HANDLER
}
```
Creator-recall bypasses entire block (creator-guard `:95-98` is sole recall authz).

**Defects caught & fixed (5 classes):**
1. **Authz-of-T conflation**: Used targetState.Id in both calls → tested against next-handler pool, not T's rule. Spec line 28 mandates current-state pool. Tests asserted wrong semantic (clause seeded on target). Caught: diff read vs spec line 28 + advisor ruling. Lesson: impl-test-contract triple verification required before green-flag.
2. **Final-state authz exception**: Gate wrapped in `!IsFinal` → any principal with coarse policy→final with zero per-transition authz. Exception belongs ONLY to WF_NO_HANDLER's empty branch. Fixed: two-call split, authz unconditional, only WF_NO_HANDLER final-aware. Lesson: guard placement scans must check ALL conditional boundaries.
3. **Creator-recall fallthrough**: Creator `if` returns only on non-creator path; creator falls through to authz-of-T (empty pool) → 403. No unit test (coverage gap). Worktree logic trace caught it. Fix: wrap entire gate in `if (!transition.IsRecall)`. Mirror in GetAvailableTransitions (recall visible to creator). 3 new tests added. Lesson: creator pathways warrant dedicated happy-path test, not coverage gap.
4. **M1 fail-OPEN**: ResolveBranchChild pre-check wrapped in `if (defResult.IsSuccess)` → transient def-load failure silently skipped gate. Fail-CLOSED invariant violated. M2: GetAvailableTransitions passed `request.UserId` as `InitiatorUserId` vs `instance.CreatedBy` (visibility/exec divergence). Dapper InstanceStateRow lacked CreatedBy—added. L1: bare `catch` no `LogWarning`. Lesson: fail-CLOSED is an invariant; every exception path is a smell.
5. **G3 fixture regression**: Subagent worktree-diffed `Tests.Integration` (clean) but NOT `Workflow.Integration.Tests` (touched handlers). 2 new fails `ExecuteTransitionContractTests.SubmitFromDraft_…` + `ConcurrencyTests.SequentialRace_…InvalidTransition`. Root: fixtures seeded workflows with zero `WorkflowTransitionAssignment` rows → gate correctly denied. Resolution: migrate fixtures to assignment-rule model via new `SeedAuthorizingAssignmentAsync` opt-in (NOT weaken gate, NOT skip assertions). Restored baseline 23p/1f/2skip. Lesson: worktree-diff EVERY suite touching changed surface, not sampled subset.

## Decisions

- **WF_NO_HANDLER = 422 ValidationError**: Empty pool = workflow misconfiguration, not actor authorization failure. Cleaner semantic (403 reserved for policy denial).
- **GetAvailableTransitions extra EF call**: Read currentState for pool-resolution instead of second repo method (KISS).
- **OutgoingAssignment in Application.Services**: Intra-Application cross-layer, G2 confirmed clean.

## Lessons Learned

1. **Subagent reports under-state files + over-state correctness.** Round 1: "1 file modified" (only test). Actual: 12 files / 862 insertions. `git diff --stat` non-negotiable before trusting "files modified" summary.
2. **"X is pre-existing" requires worktree-diff on exact suite.** Tester worktree-diffed `Tests.Integration` but skipped `Workflow.Integration.Tests` (the touched handlers). 2 regressions hid behind "pre-existing concurrency" assertion. Advisor caught it; rule: every touched surface area, not sampled subset.
3. **Tests written to wrong impl lock in wrong contract.** When impl built before semantic review, tests document the wrong contract; "green" is meaningless. Read diff against spec, not against tests.
4. **Advisor pre-finalize checkpoint caught 2 of 5 defects.** G3 baseline + M2 column-name. Treat advisor pre-finalize as hard gate on security-core slices.
5. **Fail-CLOSED is an invariant, not a goal.** Any `if (success) { …gate… }` shape is a smell—invert it. Every abnormal condition (Empty, exception, transient failure) must end in deny.

## Next Steps

**STOP / FRESH-CONTEXT MANDATED** (per plan). Slices 6-11 = separate sessions:
- **S6**: Atomic-tx + defer-save consistency (own session, fresh context)
- **S7**: Standalone 409 gate + field removal (own session)
- **S8**: M2 column-drop (irreversible, gated by Q3 re-confirm)
- **S9**: EscalationCheckJob pool-safety + aging
- **S10**: Override 3-layer + orval regen
- **S11**: Final merge gate proof

Branch unpushed; resume = same branch `feat/process-handler-resolution-p3`, `git log --oneline` to orient.

---

**Commit**: `508ed525` (P3-S5 resolver fail-CLOSED, v2.137p76, unmerged)  
**Proof**: Build 0E, G1-G4 ✓, 0 new regressions, 5 defect classes caught & fixed  
**Status**: RESOLVED + VERIFIED ✓
