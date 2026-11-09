# WF Parallel Phase 6: Claim-Bridge (Manual-Assign Guard + TaskId Projection)

**Date**: 2026-06-08 20:50
**Severity**: Medium
**Component**: Workflow Parallel Branch (Phase 6/7 — claim UX gate)
**Status**: SHIPPED (committed, NOT pushed; ahead=2)

## What Happened

P6 (claim-bridge) delivered 3 scoped pieces:

1. **RT7 manual-assign guard**: AssignTaskCommandHandler + ReassignTaskCommandHandler reject branch-child tasks (BranchChildStatusId != null) with 422 ValidationError. Branch-child tasks are pool-claim only; manual assign bypasses pool-eligibility check. Guard at command/HTTP layer only — domain task.Assign() callers (fan-out, escalation) untouched.

2. **Branch-status taskId projection**: GetBranchStatusQueryHandler OUTER APPLY selects live WorkflowTask.Id; appended long? TaskId to WorkflowBranchChildStatusDto. Additive contract.

3. **FE pool-Claim button**: ParallelBranchStatusPanel shows Claim for pending+unclaimed+task-staged child (optimistic-show; server validates pool eligibility; 403 → toast).

Shipped: `43cf02a2` feat + `c7e4f918` docs. NOT pushed (user STOP-before-push).

## The Brutal Truth

This phase felt like it should take 3-4 days based on the plan doc. It took ~1 day. Why? Because the "critical crux" (RT6) was already shipped in a prior phase (ResolveBranchChildCommandHandler:65-78 assignee gate) but the plan doc hadn't been updated. Lesson: the plan becomes stale faster than the code ships. Audit-at-slice-start caught it before we burned time re-implementing. That's the win. The other frustration: a freshly-written FE claim handler had a dead code path because the error shape didn't match what was written — code-review had to catch it. That's the smell of not running integration tests early.

## Technical Details

### RT7 Blast-Radius Verification
Grep confirmed surgical safety:
- AssignTaskCommand + ReassignTaskCommand are HTTP-only (WorkflowTasksController)
- ParallelBranchService.ExecuteForkAsync() + EscalationCheckJob call domain task.Assign() directly (no validation)
- Guard at command layer scopes exactly the manual-assign callers we intend to block
- Zero regression: full Workflow integration 104/104 (baseline 103 + 1 new taskId)

### FE Claim 403 Handler Bug
Fresh code read `err.response.status` but axios interceptor rejects a flat NormalizedApiError (.status, not .response.status):
```
// WRONG (dead code):
if (err.response?.status === 403) { toast.error(...) }

// CORRECT (post-fix):
if (err.status === 403) { 
  return { suppressAuthInterstitial: true } 
}
```
This was the PRIMARY failure path for the optimistic-show design. The global 403 interstitial would fire redundantly. Added optional `suppressAuthInterstitial` param to shared `useClaimTask` hook; callers unaffected (BP next-handler-card doesn't pass it).

### Verification Gap Caught by Code-Review
Unit test mocks workflow-api wholesale → the real hook change was executed by **zero tests** (typecheck-only). Ran FE workflow + business-process suites (65/65) to verify the real hook path + BP consumer integration.

## What We Tried

1. **Plan-doc-driven implementation**: Started from phase-06.md (4-5 days, multiple "crux" items)
2. **Audit-at-slice-start hit**: Empirical grep on ResolveBranchChildCommandHandler found RT6 already wired → scoped down to RT7 + contract + FE
3. **FE claim button with optimistic-show**: Handler reads wrong error shape → code-review catches, fix applied
4. **Unit test isolation miss**: Mocked-away the hook means unit test doesn't exercise the change → ran consumers' full suites instead

## Root Cause Analysis

1. **Plan staleness (INVARIANT 260522-A)**: The plan doc was accurate when written but the codebase moved faster than the doc. The checkpoint (P5 concurrency-fix session) already had confirmed RT6 was shipped, but we didn't re-read the plan before slicing P6. Lesson: trust empirical probe over plan-doc claims; always audit ground-truth before opening a row.

2. **HTTP error shape mismatch**: FE code was written against `err.response.status` (raw axios) but our interceptor normalizes to a flat object with `.status`. No integration test of the claim 403 path meant this dead code path wasn't caught until code-review. Lesson: when adding an error scenario, verify the actual error shape the interceptor produces, not the theoretical axios shape.

3. **Unit-test wholesale mock hiding integration**: Vitest mocks all of `workflow-api` → changes to the real hook are NOT exercised by unit tests. The fix was correct but unverified by the unit layer. Lesson: a wholesale-module-mock means your unit test does NOT cover the shared code you changed — run the consumer integration suites.

4. **Guard placement discipline**: The advisory pre-flagged the RT7 safeguard: placing it at command-handler (not domain layer) means it only affects HTTP callers, not domain callers. Grep confirmed this separation. Lesson: place a guard at the layer that scopes exactly the callers you intend to block — this eliminates blast-radius risk.

## Lessons Learned

1. **Audit-at-slice-start is real**: When a plan doc is >1d old in a fast-moving codebase, empirical probe (grep, read live code) beats plan claims. The 5-min audit saved 3-4 days of re-implementation.

2. **Error shape contracts must be verified empirically**: Writing a 403 handler against "what axios returns" vs "what the interceptor produces" is a common trap. Always trace the real error flow or run integration tests early.

3. **Wholesale mocks hide integration gaps**: When a unit test mocks an entire module, changes to that module's shared exports are NOT exercised. The fix was correct but unverified — rely on consumer suites, not the unit-mocked path.

4. **Guard placement == blast-radius control**: Placing a validation at the command handler (HTTP entry point) vs domain layer (all callers) is a deliberate scoping choice. Verify via grep before shipping; the right layer choice makes safe a change that would be risky elsewhere.

## Next Steps

1. **Push when ready**: Commits are staged (ahead=2); user decided STOP-before-push. No blocker.
2. **P7 demo seed**: Only phase remaining (demo fixture data for claim scenario). Setup only — no new code.
3. **Deferred backlog rows**:
   - **WF-PARALLEL-BRANCH-BROWSER-E2E**: Author→click→claim E2E test (needs running dev stack; carries from P6)
   - **WF-PARALLEL-ADMIN-REASSIGN-CLAIMED**: Branch-child claimed by unavailable user has no recovery path (admin-reassign-to-eligible); deferred as separate row (post-6.2 or future)
   - **WF-PARALLEL-H1-RECONCILER**: Timeout+transient-conflict reconciler (not prod-reachable, P5-FOLLOWUP last item; carries)

## Verification Summary

- **Build**: 0W / 0E (clean)
- **BE unit**: 23/23 (+4 RT7 tests)
- **Workflow integration**: 104/104 (+1 taskId projection test)
- **FE workflow + BP**: 65/65 (consumer integration — verifies the real hook + claim 403 path)
- **FE vitest panel**: 5/5 (unit isolation, post-fix)
- **Code-review**: 0 Crit / 0 High (M1 fixed in-scope; L1 applied)
- **Git**: 2 commits ahead (NOT pushed)

## Accepted Trade-Offs

1. **No admin reassign for unavailable claimant**: Branch-child claimed by a now-unreachable user has no recovery. Manual assign rejected by RT7; reassign guarded. Recovery path (admin-reassign-to-eligible) deferred. Risk: stranded assignment if claimant removed or role revoked mid-claim. Mitigation: EscalationCheckJob (future) + carry backlog row.

2. **E2E browser claim deferred**: Author→click→Claim button→pool eligibility check→claim success flow. Assertion at handler level (returns 422/403) but not via live HTTP. Running dev stack required (carry-fwd).

## Unresolved Questions

None — all findings verified or deferred to carry-fwd rows. RT7 placement (command-layer guard) verified safe via grep. Error shape fix verified via consumer suites. No blocking tech debt.

---

**Status**: SHIPPED (6/7 phases complete; P7 seed data remains)
**Commits**: `43cf02a2` feat + `c7e4f918` docs
**Push State**: ahead=2, NOT pushed (user decision)
