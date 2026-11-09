# Restore 2 Stale RED Test Guards (AuditCategoryBackfill + RlsPolicyPresence)

**Date**: 2026-06-15 15:00
**Severity**: Medium
**Component**: Test suites (Audit unit, TenantIsolation integration)
**Status**: Resolved
**Commit**: `4467fb50` (1 ahead, NOT pushed)

## What Happened

Deleted 1 RED test from `AuditCategoryBackfillSqlMirrorTests` and added 2 missing entries to `RlsPolicyPresenceTests` ExpectedPolicies array. Both stale guards were pointing at artifacts that moved (migration file deleted in squash) or counters that drifted (new RLS policies added post-array-update).

**Test before:** Audit unit RED, RLS integration RED (count mismatch 39 actual vs expected, real defect).
**After:** Audit 1/1 green, RLS 2/2 green (live SQL DB verified).

## The Brutal Truth

These weren't logic bugs — they were stale guards that stopped matching their source of truth. The frustration is that:

1. The audit migration test was asserting a file (`20260501004056_AddAuditCategoryAndSeverity_M1.cs`) that was *deliberately deleted* 5 months ago in a Phase-06 migration squash. The test survived because nobody ran the audit test suite since squash day. Running once revealed a dead assertion pointing at nothing.

2. The RLS count guard fell behind. Two RLS policies were added in May (forms version + workflow task assignment) and the array wasn't updated. This is the classic "maintenance debt" — a hardcoded constant list that diverges from what actually exists.

Both could have stayed broken indefinitely if we hadn't forced a full test run today. The backlog row guessed wrong ("BP-MVP issue") — empirical source enumeration (all 41 RLS policy generation sites) beat backlog narrative.

## Technical Details

**Fix 1: Deleted AuditCategoryBackfillSqlMirrorTests::Migration_Sql_File_Mentions_Every_Mirror_Key**

- **What it did:** Asserted `File.Exists("20260501004056_AddAuditCategoryAndSeverity_M1.cs")`
- **Why it failed:** Migration squash (commit `1f344588`) deleted 217 migration files including this one; greenfield `InitialCreateFoundationV1` now carries audit Category/Severity as first-class columns with no data-backfill needed
- **Root cause:** The `CASE WHEN 'LOGIN' THEN ...` SQL backfill in that migration was ephemeral — one-time data fix at migration time. Once squashed into foundation, the backfill logic is gone forever (no CASE in any audit migration now; grep AuditCategory/WHEN = 0 results)
- **Why the test survived:** audit test suite not in CI automation, nobody ran it post-squash
- **Kept:** The real invariant guard `Mirror_Matches_AuditCategoryMapper_For_Every_AuditAction_Constant` (mirror dict ↔ AuditAction enum drift) — this one still matters

**Fix 2: Added 2 missing RLS policies to RlsPolicyPresenceTests::ExpectedPolicies**

- **What it does:** Array lists all RLS policies that MUST exist; test enumerates actual DB policies (live SQL) and asserts presence
- **Missing entries:**
  1. `forms.FormTemplateVersions` (added by migration 20260514102007_AddFormTemplateVersion)
  2. `workflow.WorkflowTaskCandidateIndex` (added by migration 20260519015732_AddWorkflowTransitionAssignmentAndPoolTask)
- **Root cause:** Array last updated April 22 (before May migrations); no governance to sync array ↔ GenerateRlsPolicyStatements() call-sites
- **Why it failed:** Test enumerated live DB, counted 41 policies; array expected 39 → RED
- **Why it slipped:** RLS policies added in diff migrations (forms + workflow), easy to miss in cross-module review

## What We Tried

1. **Backlog diagnostic** — row claimed "BP-MVP issue"; checked BP-related migrations → false lead (3 BP policies already in array)
2. **Source enumeration** — walked all 16 `GenerateRlsPolicyStatements()` call-sites + 3 nullable-tenant helpers → found the 2 missing from May
3. **Reordered historical review** — migration date order revealed timing (April array update → May new policies added → test never re-run)

## Root Cause Analysis

**Why audit test ignored migration deletion:**
- One-off data-backfill migrations are deleted during squash (correctly)
- No migration survives to prod, so test guarding migration *file existence* is ephemeral
- Test assertion on file existence was never invalidated because test didn't run post-squash

**Why RLS policy array drifted:**
- No CI gate requiring array ↔ migration sync
- Policies added in different modules (forms, workflow) — easy to miss in module-local testing
- Test only runs when TenantIsolation.Tests executes (not a default target in all CI profiles)

## Lessons Learned

1. **Backlog-row diagnostic can be wrong.** The row guessed BP-MVP; empirical source walk (all GenerateRlsPolicyStatements sites) beat narrative. Always audit ground-truth when a backlog row is old (§INVARIANT 260522-A).

2. **Guards on deleted artifacts are stale by definition.** When you deliberately delete a migration (squash), the test guarding its file existence becomes a zombie. Check if a different guard covers the invariant before deleting the assertion.

3. **Hardcoded expected-value arrays decay.** Array constants + codegen/migration-driven sources = divergence over time. Either:
   - Make it codegen-driven (reflect into array from live source)
   - Lock it with a CI gate (diff expected vs actual, fail if unintended drift)
   - For small counts: accept manual updates + document when to update

4. **Test suite fragmentation is silent rot.** These tests don't run in default CI, so broken state persisted for weeks. Make sure all guards are in the critical-path test suite, or document why they're optional.

5. **Data-backfill (one-off) ≠ invariant (eternal).** The original audit migration had both — one-time CASE backfill + new columns. Squash removes the one-time part (correct); the invariant part lives in foundation (correct). Distinguish which is which before asserting file existence.

## Next Steps

**Immediate (done):**
- Delete `Migration_Sql_File_Mentions_Every_Mirror_Key` + unused helpers
- Add forms.FormTemplateVersions + workflow.WorkflowTaskCandidateIndex to ExpectedPolicies
- Commit + push (not yet)

**Follow-up (advisory, YAGNI unless recurrence):**
- Document RLS policy array maintenance (when/how to update) in code comment
- Consider: migration-post-hook auto-test for new RLS policies (grepped GenerateRlsPolicyStatements) to catch drift earlier
- Evaluate: TenantIsolation.Tests as pre-merge gate (currently optional in developer build)
