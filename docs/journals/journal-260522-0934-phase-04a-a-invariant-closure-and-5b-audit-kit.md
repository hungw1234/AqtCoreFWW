# Phase 4-A-A IDOR Closure + 5b Audit Kit Scope Drift & Seed Regression

**Date**: 2026-05-22 09:34  
**Severity**: Medium  
**Component**: WorkflowInstancesController + Identity.PermCodes + Phase 5b Integration Tests  
**Status**: Resolved + Lessons Extracted

## What Happened

Single PR bundled two unrelated slices:
- **4-A-A closure**: LIVE IDOR found on 5 naked routes in `WorkflowInstancesController` (Create/GetById/GetAvailableTransitions/ExportAudit/GetBranchStatus) plus sibling controllers (Assignments.List/Resolve, Delegation.List)
- **5b audit kit**: Pre-flight SQL kit (4 queries Q1-Q4) for ops empty-pool validation
- **Bug-class fold**: During integration testing, discovered that Phase 5b's new hard-block on empty-pool seed regression broke 5 pre-existing test files with degenerate seeds (no `WorkflowTransitionAssignment`)

## The Brutal Truth

We started with a memory snapshot saying "3 sibling controllers to audit." Reality: one was already fixed (`Migrations`), and we missed the actual third sibling (`WorkflowInstancesController` with 5 routes). This is not a showstopper—scout caught it—but honest assessment: **memory drift in carry-fwd lists is functionally useless without re-grepping at slice start**.

Worse: Phase 5b introduced a new fail-CLOSED behavior on creation that immediately broke 5 pre-existing integration test files. The gates didn't catch this because TDD coverage was in `Workflow.App` and `Workflow.Infra` only; the seed regression surfaced across 5 test files in different projects. We folded the fix per 4-A-B precedent (bug-class, <10min, same pattern), but this exposed a gap: **when hardening a creation path, you must sweep ALL integration seeds that touch that path, not just the test projects containing the new code**.

## Technical Details

**4-A-A IDOR**: 8 routes across 3 controllers lacked resource-ownership guards. `WorkflowInstancesController` alone had 5 naked endpoints; all 8 now enforce policy gates:
- Create → `Workflow.Instance.Manage`
- GetById/GetAvailableTransitions/ExportAudit/GetBranchStatus → `Workflow.Instance.View`
- Assignments.List/Resolve → `Workflow.Assignment.Manage`
- Delegation.List → `Workflow.Delegation.View`

New constant: `PermCodes.Workflow.Instance.Manage` (count 128 → 129).

**5b Seed Regression**: Phase 5b hardened instance creation to 422-fail on empty pool. Pre-existing test seeds in 5 files (`WorkflowInstancesApi`, `TasksClaim`, `TasksUnclaim`, `TasksInbox`, `TasksReassignAuthzAndStomp`) used degenerate factory patterns with zero `WorkflowTransitionAssignment` records. They passed before 5b, broke immediately after because they bypassed the pool-eligibility check that didn't exist. Fixed all 5 by injecting minimal assignment mock into seed.

**Test gates**: Workflow.App 533/533 | Workflow.Infra 72/72 | Integration 71/71 | Architecture 101/101 | build 0E/0W.

## Decisions Made

1. **Fold 5b seed regression fix in-slice** — per 4-A-B precedent (bug-class, <10min, no new decision, test mirror exists). Saved separate PR cycle.
2. **Expand scope from 3 to 8 routes** — scout caught WorkflowInstancesController as the true third sibling; memory false-positive (Migrations) discarded.
3. **Keep anti-tautology test pattern** — 16 new facts: each route has baseline anon-401 + nonadmin-403 proof that policy IS the gate (not authorization layer weakness).

## Root Cause Analysis

**Memory drift**: Carry-fwd lists are snapshots; 3 days elapsed since 4-A recorded "3 siblings"; in that time, Migrations was already fixed elsewhere, and context shifted. Pattern: you can't trust memory across day boundaries without re-grepping.

**Seed regression invisibility**: Phase 5b's new hard-block on empty-pool was added to **instance creation**. But the test files that seed degenerate instances are scattered across 5 different `.csproj` test targets. The PR gate for 5b (Workflow.App + Workflow.Infra) didn't run those files. The regression was a time-bomb that detonated during authz audit testing.

## Lessons Learned

1. **P6 carry-fwd lists need re-validation at slice-start**: Don't trust "3 siblings Assignments/Migrations/Delegation" verbatim. Grep `\[Authorize\]` + policy-less route patterns in all controllers at start-of-slice. One was already fixed, one didn't exist, one we missed. Treat memory as a search hint, not ground truth.

2. **SystemAdmin tests can hide policy regressions**: Code like `CreateAuthenticatedClient(roles: SystemAdmin)` passes even if you remove a policy gate (full grant). Regression surface is the `CreateClientWithPermissions([PermCode])` tests. When auditing Authz, always check that granular-perm tests exist; if missing, add them.

3. **Creation-path hard-blocks must sweep all downstream consumers**: When Phase 5b hardened instance creation, the TDD gate covered only the new code's projects. But 5 test files with old seed patterns immediately broke because they were downstream. INVARIANT: if you introduce fail-CLOSED on a creation path, run the full suite and audit seeds in ALL projects, not just your feature gate.

4. **Bug-class fold (4-A-B) extends to discovered-by-scope problems**: Original intent was "authz audit." During audit, we discovered a pre-existing 5b regression. Per 4-A-B (<10min + same pattern + no new decision), fold in. This worked well and saved a round-trip, but it shows that **audit slices often expose pre-existing defects**; plan for them.

5. **Anti-tautology policy tests have a necessary-vs-sufficient gap**: `Create_NonAdmin([View])_Returns403` proves Create needs >View. But it doesn't prove Create needs specifically Manage (could be Admin-only, or a different perm). Code-reviewer flagged: add `Create_AllPermsExceptManage_Returns403` to prove Manage is the boundary. New pattern for future Authz tests.

## Next Steps

- Carry-fwd to P6: 6 Low items (stale comments cleaned in-slice; others deferred)
- Carry-fwd P6 audits: same check on Forms module + BP assignment paths (3 controllers already known)
- 5b Seed patterns: docstring added to test factories warning re: empty-pool hard-block

## Metrics

- **Cycle time**: 3h (start-to-gates-green)
- **Approval**: 9.3/10 (0 Crit, 0 High, 3 Med in-slice, 6 Low defer)
- **Tests**: +16 new facts (anti-tautology policy proofs)
- **Code impact**: 8 routes + 1 new PermCode + 5 test seed fixes

---

**Status: DONE**
