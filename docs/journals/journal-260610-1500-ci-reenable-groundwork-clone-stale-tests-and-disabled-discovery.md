# CI Re-Enable Groundwork: Clone Stale-Tests & Disabled Discovery

**Date**: 2026-06-10 15:00
**Severity**: High
**Component**: CI/CD (GitHub Actions), Workflow.Domain clone logic, unit tests
**Status**: Blocked (3 upstream fixes required before re-enable)

## What Happened

Started with goal "re-enable CI". Ran `/aqt-next` → `/cook Scout-first` diagnostic. Discovered the GitHub Actions `CI` workflow is `state: disabled_manually` since 2026-04-13. No CI run in ~2 months. Every "shipped ahead=0" commit in that window (12+ ships) was CI-unverified.

The earlier premise ("CI frontend broken since 06-09") was moot — the job never ran at all.

## The Brutal Truth

This is infuriating. Two months of shipping without CI verification. Every "pushed ahead=0" read as "CI-clean" when the workflow was disabled the whole time. The scout-first approach saved us — it revealed the actual blocker (workflow disabled) rather than letting us spend hours on a stale-tests rabbit-hole when the real fix is admin-side.

But it also means re-enable is NOT a single fix. It's a multi-slice effort with 3 upstream blockers. We cleared ONE arc (clone), not the finish line.

## Technical Details

**CI Workflow State:**
```
gh api repos/org/AqtCoreFW/actions/workflows/CI.yml
"state": "disabled_manually"
"created_at": "2026-04-13T..."
gh run list --workflow=CI.yml
[last run 2026-04-15, all failures, no runs since]
```

**Shipped This Session:**
- `888e1fae`: Workflow.Domain clone unit-test fix. 11 `Clone()` calls missing `nextVersion` arg added in `375d682f` → CS7036 compile error under `/warnaserror`. Fix: `def.Version + 1` in stubs. **145/145 Domain tests pass.**
- `fbf17a6d`: Rewrote 3 handler unit-tests in `CloneWorkflowDefinitionCommandHandlerTests` to match delegated-design (handler now delegates deep-copy to `ReplaceGraphAsync`, clears in-memory shell). Old assertions (`result.Value.States`-based) were vacuous post-refactor. New assertions validate payload handed to delegate + explicit NSubstitute stubs for `GetForValidationAsync`, `GetParallelBranchesForCloneAsync`, `GetMaxVersionByKeyAsync`. **640/640 Application tests pass.**
- Workflow.Application + full-solution build: **0W/0E under /warnaserror**.
- Changelog + backlog docs sync commits.

**Pre-Existing Failures in Full Suite (NOT clone-related):**
- Reporting.Infra `SqlValidatorRegexTimeoutTests`: SQL-validator Regex timeout returning `PATTERN_TIMEOUT` under CPU contention. **Passes isolated.** = Full-suite flake.
- Reporting.App `SharedStateTests`: isolation/shared-state contamination. **Passes isolated.** = Full-suite flake.
- TenantIsolation integration leak: test FQN lacks "Integration" substring → `!~Integration` filter misses it in unit job. Runs in no-DB job → SQL errors. = Scoping bug.
- Audit migration squashed: SQL-mirror invariant obsolete. = Deterministic stale test (only 1 of 4).

## What We Tried

1. **Run each failure in isolation** (3 separate runs per test). Separated flakes (pass-alone = flake) from stale (consistent fail = stale).
2. **NSubstitute auto-value reliability test** (wholesale mock vs explicit stubs). Found: auto-values unreliable for tuple/dict returns (null in isolation, substitute in full-class) → NRE flake. Switched to explicit stubs.
3. **Delegated-design unit-test rewrite** (old aggregate-result assertions vs new delegate-payload assertions). Old: vacuous. New: stronger, verifies what the handler hands downstream.

## Root Cause Analysis

**Why was CI disabled and left disabled for 2 months?**
Likely pre-April blocker (unverified — workflow state doesn't log reason). Subsequent ships assumed "no runs" = "test flakes" not "workflow disabled". Scout-first bypass (jump to gh api + run history) caught it; grepping test names would have wasted 4h on a non-blocker.

**Why did clone arc have stale tests?**
The clone handler was refactored (PR#116 merged 260609) to delegate to `ReplaceGraphAsync`. Unit tests were NOT updated; they still asserted on in-memory aggregate state that is now intentionally cleared. This is a post-merge debt, not a pre-existing flake. Necessary fix before CI can run.

**Why are 3 other failures not clone-related?**
Contention flakes (Reporting.Infra/App) + scoping bug (TenantIsolation) + obsolete test (Audit) are orthogonal. They will surface when full suite runs; re-enable is blocked on fixing these in parallel.

## Lessons Learned

1. **"Pushed ahead=0" ≠ "CI-verified".** Always check workflow state via `gh api repos/.../actions/workflows/{name}`. Last commit date ≠ last run date. A disabled workflow will silently pass every push.

2. **Run each failure in ISOLATION before diagnosing.** "4 flakes" ≠ "4 stale-tests". Isolation run = ground truth. 3 of 4 failures here pass-alone (flakes), only 1 is stale. Full-suite contention makes diagnosis unreliable.

3. **Delegated-design unit-tests assert on delegate inputs, not delegator state.** Old pattern: aggregate-result assertions (now vacuous). New pattern: stub the delegate, assert the payload handed to it. Much stronger.

4. **NSubstitute auto-values are unreliable for complex types.** Tuple/dict returns don't auto-substitute consistently across isolated vs full-class runs. Always stub deps explicitly.

5. **Characterize accurately under pressure; surface corrected scope.** User approved "fix all 4 stale-tests" (estimated 1-2h). Investigation revealed 3 are NOT stale (contention flakes + scoping bug) and require separate effort. Rather than bulldoze a wrong fix, surfaced: "only 1 deterministic, scope is 3× deeper, 3 blockers are separate." Accuracy > false-finish.

## Next Steps

**CI Re-Enable Blockers (filed `CI-DISABLED-SINCE-APRIL` in backlog):**
1. **.NET full-suite contention mitigation** (mirror vitest `maxWorkers:2` fix). Reporting.Infra/App flakes pass isolated; need CPU/thread cap in CI matrix or test-grouping strategy.
2. **Integration-test scoping fix** (`TenantIsolation` test FQN includes "Integration" substring so `!~Integration` filter catches it in unit job).
3. **Obsolete Audit migration test removal** (SQL-mirror invariant no longer valid post-squash; test deterministically fails).

**Who owns it:** Lead decides slice-order. Clone arc is UNBLOCKED for workflow re-enable; contention+scoping+obsolete are independent tasks.

**Timeline:** Re-enable is multi-slice (not this session). Current session cleared ONE arc (clone). Honest outcome: CI still disabled, but the stale-test debt is resolved and 3 upstream blockers are now explicit.

---

**Verification:**
- Workflow.Domain: 145/145 ✓
- Workflow.Application: 640/640 ✓
- Full-solution build: 0W/0E ✓
- Commits: `888e1fae` + `fbf17a6d` + docs sync pushed origin+bitbucket ahead=0
