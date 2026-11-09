# Process Handler Resolution P3-S1: Coarse Authorization Policy Gate

**Date**: 2026-05-19 12:31
**Severity**: High
**Component**: Workflow/BusinessProcess ExecutionContext
**Status**: Resolved

## What Happened

P3 Session-1 delivered the foundational coarse-grained authorization gate for the highest-risk PR in the program—Process-Handler-Resolution Phase 3. Implemented `[Authorize(Policy = PermCodes.Workflow.Task.Process)]` + `[ProducesResponseType(403)]` on 3 RT3-M1 execute surfaces:

1. `WorkflowInstancesController.ExecuteTransition` (POST)
2. `WorkflowInstancesController.BatchTransition` (POST)
3. `BusinessProcessesController` transitions endpoint (POST /runs/{id}/transitions)

The 3rd surface AND-stacks with pre-existing `Bp.View` policy (defense-in-depth; earlier tester concern). Constant verified in `PermCodes.cs:248`, in `PermCodes.All[]` at index 45, auto-registered via `Identity.ModuleRegistration.cs:23-25` (no seeder mutation required). Failing-default 403 merge-gate test (4 assertions: 3 surfaces RED→GREEN + positive control vs false-green contamination) locked the gate. **No migration, resolver, defer-save, or FE RPC removal in this slice.**

## The Brutal Truth

This session's real win was not the 20 lines of `[Authorize]` attributes. It was **reframing what "indivisible PR" actually means** and refusing to do 5 days of work in one turn. The original spec framed P3 as a single monolithic session: migration + resolver + 3 execute gates + delete legacy surfaces + FE removal + 12 tests. Trying that meant context exhaustion by day 2, reckless commits by day 3, and a review disaster by day 4. 

The advisor's clarification was critical: "indivisible PR" is a **merge property**, not a session property. The HTTP 403 merge-gate is what prevents partial ship—each session lands on the unmerged feature branch. That property flipped the entire execution model from "impossible all-at-once sprint" to "disciplined multi-session slice-and-lock strategy."

Accepting that reframe felt like giving up. It wasn't. It was the difference between a shippable PR and a 20-day grinding mess that ships broken.

## Technical Details

**Verified Blast:**
- Architecture.Tests: 101/101 GREEN (no RequiredPermissionCode allowlist work; RT3-H4 "≥12 RowVersion handlers" was miscount, concurrency is config-level IsConcurrencyToken, zero Application/Commands naming)
- Workflow.Integration: 22/22 GREEN + 2 SKIP (no HTTP ripple; WebAppFixture in-process calls bypass HTTP authz—architectural truth: [Authorize] gates HTTP callers, NOT IWorkflowModuleClient contract)
- Build: 0 warnings / 0 errors
- 1 pre-existing test failure: `GetAvailableTransitions_ForRunningInstance` (GET endpoint scope ∉ P3-S1; Dapper WorkflowTransitionDto ctor bug, worktree-proved identical on parent bb73ab12)

**Recon Corrections (original spec stale):**
- Blast scope: A=14 / B=5 / C=9 / D=12 / E=11(frozen) / F=9(doc) ✓
- IWorkflowModuleClient contract: 11→10 (dependency-map had 8-vs-7 confusion) ✓

**Code Surface:**
```csharp
// WorkflowInstancesController
[HttpPost("{instanceId}/transitions/{transitionId}")]
[Authorize(Policy = PermCodes.Workflow.Task.Process)]
[ProducesResponseType(403)]
public async Task<ActionResult<ExecuteTransitionResponseDto>> ExecuteTransition(...)

// BusinessProcessesController
[HttpPost("{id}/transitions")]
[Authorize(Policy = PermCodes.Workflow.Task.Process)]
[Authorize(Policy = PermCodes.BusinessProcess.View)]
[ProducesResponseType(403)]
public async Task<ActionResult<BpTransitionResponseDto>> ExecuteProcessTransition(...)
```

Failing-default 403 gate test:
```csharp
[Theory]
[InlineData(nameof(WorkflowInstancesController.ExecuteTransition))]
[InlineData(nameof(WorkflowInstancesController.BatchTransition))]
[InlineData(nameof(BusinessProcessesController.ExecuteProcessTransition))]
[InlineData(PositiveControlForFalseGreen)] // sanity check: normal callers still 200
public async Task Surfaces_Without_Process_Permission_Return_403(...)
```

## What We Tried

1. **Tried**: Bundling all P3 into one session. **Failed**: Recognized context exhaustion + reckless-commit risk. **Pivot**: Reframed "indivisible" as merge property, not session property.

2. **Tried**: Accepting subagent's reason-assertion that `GetAvailableTransitions` failure was pre-existing. **Failed**: Pattern-match memory false-green ×N. **Proved**: Worktree-compared parent bb73ab12 byte-identical, confirmed pre-existing Dapper ctor bug unrelated to P3-S1.

3. **Tried**: Expanding slice scope to include `ResolveBranchChild` (4th execute surface). **Rejected**: Over-reach mid-session (scope-creep is how PRs die). Logged as **RT3-M1-F1 tracked decision**.

## Root Cause Analysis

The original P3 spec's framing was aspirational, not realistic. Breaking it by merge-gate behavior—rather than by arbitrary "session convenience"—meant each session could be properly sized, tested in isolation, and reviewable without exhaustion. The mistake would have been pretending one session could safely cover 5 days of security-critical work.

The `GetAvailableTransitions` pre-existing bug was a pattern I'd seen before: tester sees RED, asserts "pre-existing" without proof, I accept the reason. Works 80% of the time; fails catastrophically when it doesn't. Worktree evidence is the only reliable proof in a branched codebase.

RT3-M1-F1 (the 4th execute surface) is a genuine security enumeration under-count. Did NOT fix inline—that would be scope-creep + risk-stacking. Logged it explicitly so the fine-gate session can make an informed decision: extend 3→4 or document exclusion rationale. This is the right discipline: find security issues, track them explicitly, let the next session decide with fresh context.

## Lessons Learned

1. **"Indivisible" is a merge property, not a session property.** Reframe impossible multi-day slices as multi-session branches locked by merge-gates. This flips context exhaustion to controlled slicing.

2. **Reason-assertion is not proof.** Subagent says "it was pre-existing"? Worktree compare against parent. False-green memory compounds fast; proof-based discipline scales.

3. **Scope-creep mid-session is how security PRs fail.** Found RT3-M1-F1 (4th surface under-count)? Log it, don't fix it in-turn. Let the next session decide with fresh context + explicit user confirmation.

4. **Architectural truth matters**: [Authorize] gates HTTP callers, not in-process contract calls. WebAppFixture bypass is not a bug; it's a configuration boundary. Workflows calling IWorkflowModuleClient internally bypass HTTP authz—that's intentional, separate-layer concern.

## Next Steps

P3 remains **Pending** overall. 6+ sessions remain, each requiring explicit user confirmation:

1. **P3-S2**: Migration (Testcontainers.MsSql, own dedicated session)
2. **P3-S3**: Defer-save + concurrency regression gate (own session)
3. **P3-S4**: Resolver fail-CLOSED behavior
4. **P3-S5**: QueryAllowedTransitions legacy surface DELETE
5. **P3-S6**: Override 3-layer thread + GetUserActiveStatusAsync live
6. **P3-S7**: EscalationCheckJob pool-safety + aging
7. **P3-S8+**: FE orval + field/config/DTO deletions + RT3-M1-F1 decision
8. **P3 Final**: Code review + merge gate validation

Each session is standalone; branch stays unmerged until full PR gates PASS.

---

**Commit**: `ae3f1ddb` (P3-S1 coarse authz, unmerged feature branch)
**Proof**: 4/4 gate assertions + Architecture 101/101 + Workflow.Integration 22/22
