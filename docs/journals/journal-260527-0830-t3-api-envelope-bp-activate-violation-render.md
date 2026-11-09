# T3-API-ENVELOPE-BP-Activate Violation Render

**Date**: 2026-05-27 08:30
**Severity**: Low
**Component**: Business Process activation handler + workflow graph designer FE
**Status**: Resolved

## What Happened

Shipped v2.137p120 closing T3-API-ENVELOPE-FE-RENDER carryforward tasks (a, c, d):
- BP-Activate: `ActivateBusinessProcessCommandHandler` projects graph violations to wire via reserved `PublicMetadata` (cast-pattern match to p118 SaveGraph pattern); 2nd call-site validates architectural consistency
- FE multi-line violation toast on `handleActivate` (mirrors `handleSave` from p118)
- Designer canvas state-node highlight on save-fail: error-glow + corner badge + tooltip + aria-invalid; violation.stateName→state.name mapping verified against WorkflowGraphValidator.cs
- transitionActionName inclusion in violation bullet (handles 4 presence combos)
- Contract doc: WorkflowGraphViolationDto (BE-side) carries no Metadata field → BP violations undecorated (acceptable; documented)

Commits: `673c776f` (feat code+tests) + `4416bf47` (docs). Tests: BP.App 95/95 + arch 6/6 + FE 19/19. Review 9.3/10 APPROVE-WITH-NITS.

## The Brutal Truth

I made an orchestration mistake that nearly surfaced as a false-positive regression. Spawned tester + code-reviewer in **parallel**, not realizing the git-state concurrency hazard. The tester stashes the working tree (baseline check), the reviewer reads `git status`/`git diff` during the stash window and sees a clean tree, reports BLOCKED "deliverable does not exist / all 3 criteria unimplemented". The frustrating part: tester returns "FE 19/19 present"; reviewer insists "nothing implemented" with confident file:line citations. Direct contradiction, both agents sincere. Recovery took 15 minutes of `git status` / `git stash list` forensics to realize the race, then solo re-run of reviewer. This is embarrassing because the orchestration protocol exists specifically to prevent this.

Secondary frustration: tester reported full-solution build FAILED (7 CS4014 errors). I panicked thinking I broke Workflow.*.Tests. Turned out the errors were pre-existing tech-debt in untouched assemblies; targeted assembly (BP.Application.Tests) compiled clean. Lost 10 minutes trusting the aggregate error before drilling down.

## Technical Details

**Git-state race window:**
- Tester: `git stash` → run assembly-specific tests → `git stash pop`
- Reviewer (concurrent): `git status` + `git diff HEAD` at T+0.5s → observes working tree in stashed state → reports zero deliverables
- Both agents complete; reports are internally self-consistent but contradict each other on observable fact (code presence)

**Build scope issue:**
- Full-solution build `dotnet build` touches Workflow.*.Tests assembly files NOT in slice
- Pre-existing `await Task.Delay(...)` without `await` → CS4014 x7 in unmodified code
- Targeted build `dotnet build BP.Application.Tests` → clean, 95/95 pass
- Lesson: build-failure severity depends on failure location; pre-existing != slice-regression

## Root Cause Analysis

**Orchestration error:** Concurrency is safe for file-disjoint subagents (Phase 01 BE + Phase 03 FE ran parallel fine); it is NOT safe when subagents mutate git working-tree state. The tester's stash mechanism is a **git-state holder** — it acquires exclusive access to the working tree. Running a diff-aware reader (code-reviewer) concurrently creates a race. I didn't model subagent execution as a resource-contention problem.

**Build error conflation:** Treated aggregate solution build as source-of-truth for slice health. Should have (1) noted pre-existing CS4014 warning count as baseline, (2) targeted affected assembly only, (3) confirmed delta.

## Lessons Learned

1. **Git-mutating subagents are exclusive resources.** Any subagent that calls `git stash`, `git checkout`, `git reset` (baseline checks, worktree init, rollback) must run sequentially w/ all other agents. Don't parallelize tester + reviewer + any git-reading agent. Cost: 5-10 minutes added latency; benefit: eliminates false-positive "contradictory reports" class entirely.

2. **Delegate per-assembly build, not solution-wide.** When a tester reports "build failed," ask: is the failure in the touched assembly or in unmodified code? Tester should target `--filter` or explicit assembly path. Pre-existing tech-debt in untouched tests is not actionable during a slice delivery.

3. **Backlog drift is real but small.** Filed "zero FE work" was drifted — `handleActivate` did need ~10 LOC generic-error handling replaced with violation toast. Slice-start audit caught it (wasn't a failure), but good reminder: backlog estimates decay even across one day if not re-scouted at subagent-assign time.

## Next Steps

- Filed WF-BUILD-CS4014-CLEANUP (P3 tech-debt, ~30min): root out pre-existing await warnings in Workflow.*.Tests before they cause future false-positives
- Sequential orchestration for tester+reviewer henceforth (or split reviewer into two: code-quality + integration, run quality in parallel w/ tester, integration solo after)
- Carry-fwd: Phase-specific assembly targeting in tester delegation (e.g., "run tests in `BP.Application.Tests` only, skip other test assemblies for this slice")

**Files modified (this slice):** `BP.Application.Tests`, `BP.Application`, `src/web/pages/`, `src/web/components/business-process-detail-page.tsx`, `WorkflowGraphValidator.cs` (read-only verification)
