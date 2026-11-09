# Oracle 19c Migration Plan: Red-Team Validation + Dual-Branch Lock-In

**Date:** 2026-04-25 08:57
**Severity:** High (blockers found + locked)
**Component:** Oracle 19c migration planning (`plans/260424-2207-oracle-19c-migration/`)
**Status:** Resolved — plan revised & committed

## What Happened

4-cycle validation of Oracle 19c migration plan (created yesterday). Red-team analysis exposed 4 BLOCKERS + 7 warnings. User locked Enterprise edition decision. Subsequent cycles revised factual claims (RowVersion counts, RLS scope, DbContext inventory) and introduced dual-branch strategy for implementation.

**Cycle 1 (red-team):** brainstormer found:
- **B1:** Docker image `gvenzl/oracle-free:19-full` does NOT exist (only 23-* tags available)
- **B2:** VPD/DBMS_RLS = Oracle Enterprise ONLY (Free/XE not supported) — 45 RLS policies untestable on dev
- **B3:** Plan claims 3 RowVersion entities, reality **9 entities + 4 DTO records**
- **B4:** RLS scope off: claims 29 policies, actual **45 tables / 17 helper calls**; DbContext: claims 16, actual **21** (16 module + 5 shared)
- 7 warnings (NVARCHAR2(4000) edge case, BulkCopy TransactionScope mismatch, TDE wallet, etc.)

**Cycle 2:** User locks Q1 → **Oracle 19c Enterprise for prod + CI** (eliminates B1+B2)

**Cycle 3:** Scout verifies Q2-Q5:
- **Q2 (HasMaxLength 8000):** EXTENDED mode preserves B-tree index on 4/6 text cols; 2 JSON cols get CLOB+IS JSON bonus
- **Q4 (Hangfire user):** separate `hangfire_user` account (defense-in-depth tenant isolation)
- **Q5 (OrgDbContext duplicate):** `AqtCoreFW.Organization/` (no `.Infrastructure`) is DEAD CODE — Program.cs:41+64+230+337 bind explicitly to `.Infrastructure`. Recommend DELETE.
- User approves all 3 → planner revises 9 files

**Cycle 4:** User adds Session 2.5 dual-branch strategy:
- Branch name: `oracle-19c` (not `feat/oracle-19c-migration`)
- Sync cadence: per-feature (main merge → manual port to oracle-19c within 2-3d)
- CI matrix: single-DB per branch
- Phương án 2.B locked: commit plan to main → create `oracle-19c` from main@updated
- Planner expands phase-08 (oracle-conversion-runbook.md, convert-sqlserver-to-oracle.ps1, PR template, drift-detect.yml)
- 2 drift fixes: line 7 frontmatter branch ref + line 116 Rollback section

## The Brutal Truth

Plan validation WAS a casualty-free dry run — blockers found at whiteboard scale, **not after 2 sprints of impl work.** The alternative (skip red-team, find B1+B2 in phase-01) would have been a 4-week dead-end.

However: **subagent reports are intent, not outcome.** Planner said DONE both times but left drift (uppercase note in phase-01, 2 old branch refs in phase-08). Independent grep verification after every subagent edit is non-negotiable now.

The factual gaps (3→9 RowVersion, 29→45 RLS, 16→21 DbContext) point to a deeper issue: **plan creation was based on static assumptions, not codebase scan.** Grep counts must lock before planner writes, not after. This is expensive time-wise (10 min per phase file to scan) but cheaper than landing in phase-01 with 30% claim variance.

## Technical Details

**4 BLOCKERS (Session 2):**

| ID | Finding | Impact |
|----|---------|--------|
| B1 | `gvenzl/oracle-free:19-full` tag missing | Docker build fails Phase-01.1 |
| B2 | VPD only Enterprise Edition | 45 RLS policies untestable on Free CI container |
| B3 | RowVersion count off 3→9 | Migration script underscoped |
| B4 | RLS scope off 29→45 tables | DbContext inventory incomplete |

**Decisions locked (Session 2 + 2.5):**

```
D1: Edition           = Oracle 19c Enterprise (container-registry.oracle.com/database/enterprise:19.3.0.0)
D2: NVARCHAR2(4000)   = MAX_STRING_SIZE=EXTENDED + CLOB+IS JSON for 2 JSON cols
D3: Hangfire user     = separate `hangfire_user` account (privilege isolation)
D4: Org legacy        = DELETE AqtCoreFW.Organization/ (no suffix) — dead code
A1: Branch name       = oracle-19c (no feat/ prefix, long-lived)
B1: Sync cadence      = per-feature port (main merge → 2-3d to oracle-19c)
C1: CI matrix         = single-DB per branch
```

**Artifacts:**
- Commit 85d1f115: 9 plan files updated (1012 insertions, 337 deletions)
- Branch `oracle-19c` created locally @ 85d1f115 (not pushed)
- Memory `project-state.md` updated: Current Branch + Session Hints

## What We Tried

1. **Initial plan (Session 1):** top-down design, no codebase scan → factual drift
2. **Red-team validation:** identified 4 blockers + 7 warnings → forced factual recount
3. **Subagent revisions:** planner fixed counts and locked decisions 4 times (after each cycle)
4. **Trust-but-verify:** grep-verified all Cycle 3 findings (9 RowVersion entities, 21 DbContext, dead OrgDbContext) before locking

## Root Cause Analysis

**Why blockers existed:**
1. Oracle image availability assumption not verified against Docker Hub tags
2. VPD = Enterprise-only fact not researched upfront (team assumption: Free tier adequate)
3. Plan counts (RowVersion, RLS, DbContext) based on static code reading, not automated grep scan
4. Dual-branch strategy added post-plan (user request) → phase-08 created retroactively

**Why drift persisted in subagent reports:**
- Planner marked DONE without independent verification of its own edits
- Line edits (branch refs) fell through cracks of diff review
- Orchestration protocol trust-but-verify not enforced before commit

## Lessons Learned

1. **Red-team plan validation = 4× ROI:** Surfaced 4 blockers that would have torpedoed phase-01 if skipped. Confirmed Memory feedback-red-team-before-implement.md.

2. **Plan factual claims must grep-lock before planner writes:** Not after. Counts (RowVersion, RLS, DbContext, Dapper, JSON) were systematically off by 2-3×. 10-min codebase scan per phase < 4-week regression later.

3. **Subagent reports describe intent, not outcome:** Both planner invocations left drift (uppercase note, branch ref typos). Independent grep sample mandatory after every subagent edit. Trust-but-Verify is not optional for mutation work.

4. **Dual-branch ≠ dual-provider abstraction:** Distinguish lifecycle choice (branches) from runtime abstraction (DI). Plan rejected abstraction correctly but user can still dual-branch — orthogonal concerns.

5. **Oracle VPD is team tax forever:** Once locked, 45 RLS policies = parallel test pain (DisableParallelization mandatory), BulkCopy TransactionScope collision (regression test addition), TDE wallet ceremonies, 90-day SSO secret rotation on CI image. Nontrivial maintenance.

## Next Steps

**If Oracle phase-01 starts next session:**
1. Branch to `oracle-19c` (already created locally)
2. Execute phase-01 (Docker build + schema scaffolding) — 1-2d turnaround
3. Parallel port of concurrent main features during phase-02..07 (dual-branch sync burden grows here)
4. Phase-08 (runbook + drift detection) locks CI/CD safeguards

**Risk notes:**
- VPD parallel test race (xUnit DisableParallelization mandatory)
- BulkCopy TransactionScope mismatch (regression test needed)
- TDE wallet AUTOLOGIN ceremony
- Oracle SSO secret rotation 90-day CI window
- Conversion churn: every main feature = manual port work

**Memory sync (already done):**
- project-state.md: Current Branch + Session Hints updated
- No docs/ changes required (plan is reference docs, not code)

## Unresolved Questions

None. All Q1-Q5 answered. All 4 blockers resolved (D1 locks Enterprise). Dual-branch strategy finalized (A1-C1). Ready for implementation.
