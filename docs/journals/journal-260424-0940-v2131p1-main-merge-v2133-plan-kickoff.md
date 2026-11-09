# v2.131p1 Main Merge + v2.133 Plan Kickoff (3-Act Session)

**Date**: 2026-04-24 08:47–09:40 Saigon
**Severity**: High (silent branch divergence caught)
**Component**: Repo state, branch strategy, plan kickoff
**Status**: Resolved / Plan active

## What Happened

Three sequential acts: (1) finalize v2.131p1 demo artifacts with drift repair, (2) paranoid state audit exposing 2-way branch divergence, (3) merge to main + v2.133 async-workflow plan kickoff.

Act 1 took 13 minutes, act 2 revealed critical issue (missing v2.131 seeder code on branch thiennd/busy-meitner-dba65a), act 3 resolved via merge commit + plan delegation.

## The Brutal Truth

**08:55 moment**: Commit `efdea7c9` materialized while I was verifying docs drift. Wasn't my tool call. Had to grep logs to confirm it matched my edits (it did), not a corruption. This exposed a hard truth: Claude Code worktree + parallel user git activity = silent state shifts. I claimed "finalize complete" at 08:50 without verifying actual branch HEAD. User's follow-up audit demand was justified.

**09:00 finding**: Trust-but-verify saved us. Paranoid audit caught that `origin/main` and branch HEAD were 2-ahead/2-behind **diverged**. Branch had v2.131 label in docs but was missing actual code:
- 28 files only on main: CasesBenchSeeder/, DistributionSampler.cs, TimelineComputer.cs, VnResolutionReasonDictionary.cs, RowBlueprint.cs, + 7 test files
- Branch would fail to build without merge
- Docs already claimed v2.131 SHIPPED, but code wasn't present — silent contradiction

This felt bad. We were seconds away from promoting incomplete code to main.

## Technical Details

**Divergence anatomy:**
```
origin/main:  86db7ffd [v2.131 seeder] ← b4491508 [PR #55 squash-merged]
              |
              └─ merged into → 448a2c99 [merge commit, 2 parents]
                                |
thiennd/busy-meitner-dba65a: efdea7c9 [v2.131p1 demo artifacts k6 epsilon] 
                              e99c2824 [journal entry]
```

Cause: worktree auto-branched from commit before PR #55 landed on main; then demo-artifact work committed to branch; main moved forward with seeder code.

**Conflict resolution**:
- 4 files conflicted: backlog.md, dependency-map.md, project-changelog.md, project-roadmap.md
- Strategy: `git diff origin/main:file HEAD:file` showed HEAD = content superset (only additions, no deletions)
- `git checkout --ours` on all 4 → merge commit `448a2c99` created, clean

**Code integrity audit (6 dimensions)**:
1. Working tree: clean
2. Index: 0 staged conflicts post-merge
3. Reflog: all commits reachable via origin/*
4. Branch state: main now at df0ec586 (post-squash)
5. Backup tag: `backup/pre-merge-v2131p1-260424` @ efdea7c9 preserved
6. Artifact cross-ref: 7-file dependency-map + memory index consistent

## What We Tried

1. **Trust hook startup report** → FAILED. Hook said branch "main", but was actually thiennd/busy-meitner-dba65a. Lesson: `git branch --show-current` non-negotiable before critical ops.

2. **Commit trust-by-timestamp** → INSUFFICIENT. efdea7c9 appeared @ 08:55:56 during my session. Verified via `git log --oneline --grep="k6 epsilon"` + blob hash match to my Edits.

3. **Manual 4-way merge via editor** → AVOIDED. Diff-based superset analysis proved safe one-liner: `checkout --ours` for all conflicts.

## Root Cause Analysis

**Why branch diverged silently:**
- Worktree created from commit before PR #55 landed
- PR #55 squash-merged on main asynchronously (user action or background agent)
- Demo-artifact work committed to branch after fork point
- No rebase attempt between branch and main → status quo bias
- Hook startup status "main" was cached/wrong, masking real branch name

**Why docs claimed v2.131 but code wasn't there:**
- v2.131 seeding code shipped to main via PR #55 merge
- Demo-artifact session started from older commit, added docs claims about v2.131 artifacts
- Commit message and docs label not cross-checked against actual code presence on branch

## Lessons Learned

1. **Worktree is not a free branch guarantee.** Hook report is advisory, not authority. Always `git branch --show-current` + `git rev-list --left-right origin/main...HEAD` before assuming state.

2. **Trust-but-verify is force multiplier.** Grep + `git diff --stat` caught the superset relationship in 90 seconds. Same operation manually = 30 min error-prone work.

3. **Backup tags before merge are insurance, not paranoia.** Merge commit succeeded because we had a clear rollback point. Psychological safety = faster decisions.

4. **Docs superset ≠ code superset.** Separate audit streams: one for docs labels, one for actual build artifacts. Cross-ref at finalize, not at claim.

5. **k6 epsilon p95=24s is hardware-bound, not app bottleneck.** v2.133 research found ~15ms perf gains, nowhere near 48× gap closure. Don't optimize for wrong target metric.

## Next Steps

1. **Main branch is clean** (df0ec586, post-squash). No outstanding work.
2. **v2.133 plan active**: `plans/260424-0922-v2133-async-workflow-publish/phase-01-outbox-dispatcher-wire.md`
3. **Start phase-01** (2d): outbox dispatcher wire. Research report: `plans/reports/researcher-260424-0913-v2133-bottleneck-investigation.md` (3 findings, MVP 3–5d estimate).
4. **Deferred to v2.134**: DbContext interceptor refactor (blocker for pooled context).

**Unresolved Q's for implementation:**
- MassTransit EF Outbox production readiness for phase-02c integration?
- JWT vs reference token strategy in k6 Epsilon benchmark — affects phase-01 seeder?
- Config flag priority over DB feature flag on conflict — Cases:DeferWorkflowCreation vs feature:cases.workflow-delegation?

---

**Session artifacts:**
- research: 298 LOC (3 findings)
- plan: 407 LOC (overview + 2 phase docs)
- memory/docs: 4 files synced
- backup tag: `backup/pre-merge-v2131p1-260424` (safety net)
