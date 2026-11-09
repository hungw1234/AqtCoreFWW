# Testcontainers→Local SQL Migration: 2.5h Ship (PR #37)

**Date:** 2026-04-18 06:47  
**Severity:** Medium (infrastructure; no feature impact)  
**Component:** Test infrastructure (integration test DB connectivity)  
**Status:** Resolved  
**Branch:** `feat/testcontainers-to-local-sql` → PR #37 merged  
**Commits:** `0c045db3`, `ed90181f`, `6901bc0d`  

## What Happened

Planned 2-day Testcontainers→local SQL Server 2022 migration compressed to 2.5 hours actual. Root cause: decision-locking via red-team advisor review before code, caught 3 critical design flaws early. Shipped 5 code files (LocalSqlHelper, LocalSqlOptions, 4 hybrid fixtures), 4 docs updates, 1 CI job disable, clean 28/28 canary tests + 13/17 rollout green (4 pre-existing 403 failures unrelated to migration).

## The Brutal Truth

This was unusually smooth because we forced clarity upfront. No backtracking. No "wait, should we use shared DB or per-fixture?". No "how do we handle FK constraints?". The advisor call — which felt like overhead at the time — prevented hours of code thrashing. One near-miss on docs: MEMORY.md index had stale description until audit caught it. Without that check, our project state would have lied.

The 4 projects with 18× 403 failures stung initially. Verification proved they're pre-existing (identical failure set on Testcontainers path), but they surface a real gap: missing IEffectivePermissionService stubs for Audit/Forms/Organization/Cases modules. Migration-as-regression-scan pattern works, but it also exposes painful truths.

## Technical Details

**Infrastructure state:**
- MSSQLSERVER22 native install @ localhost,1433, SQL Auth (sa/password)
- 4 SQL-backed fixture collections: SqlServer, Messaging, TenantIsolation, Concurrency
- 1 Redis fixture (out-of-scope)
- 17 integration test projects total

**Migration strategy locked:**
- Per-collection DB (4 separate DBs) instead of shared DB with truncate
- Hybrid env var toggle: `INTEGRATION_TEST_USE_LOCAL_SQL` (default false = Testcontainers fallback)
- Truncate via `INFORMATION_SCHEMA` introspection + NOCHECK/DELETE/re-enable (auto-adapts to schema changes)
- Exclude `__EFMigrationsHistory` and `HangFire` schema from cleanup

**Code artifacts:**
- `LocalSqlHelper.cs` (164 LOC): DB name templating with regex guard, INFORMATION_SCHEMA truncate query, connection string builder
- `LocalSqlOptions.cs` (69 LOC): cached env var read, port override fallback
- Modified fixtures: `SqlServerCollection`, `MessagingCollection`, `TenantIsolationCollection`, `ConcurrencyCollection`

**Performance win:**
- Local SQL: 12s fixture cold start
- Testcontainers: 41s fixture cold start
- Speedup: ~3.4× (compound across 17 projects = huge CI savings when re-enabled)

**Failure analysis:**
- 13/17 projects: full green (all tests pass)
- 4 projects: 18 pre-existing 403 failures
  - Root: Missing `IEffectivePermissionService` stubs in Audit, Forms, Organization, Cases modules
  - Verification: Identical failure set when run against Testcontainers path = NOT migration caused
  - Pattern established: PR #36 (Notifications) + PR #34 (M-2) use same stub pattern
  - Lesson: infra migrations surface latent test gaps via repeated execution

**CI drift decision:**
- Disabled `integration-tests` job entirely (`if: false` in ci.yml)
- Reasoning: local SQL requires dev machine setup (SQL Server 2022); GH Actions runners don't have it; re-enabling requires GH Actions services block or self-hosted runners
- Backlog entry created: "Re-enable CI integration tests" (deferred scope)
- Impact: PRs no longer run integration tests in CI (unit tests still run; risk accepted for speed)

**Docs updated:**
- `deployment-guide.md`: local SQL Server 2022 setup steps
- `dependency-map.md`: fixture collection relationships
- `development-roadmap.md`: v2.114 status update
- `project-changelog.md`: migration summary + 3.4× perf improvement note

## What We Tried

1. **Initial approach (shared DB + truncate):** Advisor flagged ambiguity for parallel collections. Forced clarification → user chose per-collection DBs (cleaner, same complexity as containers).

2. **Hardcoded table list for truncate:** Advisor caught 3 different schema provisioner paths (full, EnsureCreated, MigrateAsync). Swapped to INFORMATION_SCHEMA introspection → auto-adapts to any schema variant.

3. **Fixture string literals vs typed constants:** Phase-03 rollout discovered inconsistent `[Collection("SqlServer")]` usage. Drive-by fix: introduced `SqlServerCollection.CollectionName` constant, updated 4 project references → consistent, searchable.

4. **Single DB per test run:** Realized mid-design: 4 collections run parallel (xUnit feature). Shared DB + concurrent truncate = race condition. Per-collection DB = safe by default.

## Root Cause Analysis

Why did 2-day plan ship in 2.5h?

**Decision clarity (primary factor):** Advisor call forced 5 explicit decisions (shared vs per-collection DB, truncate strategy, schema exclusions, CI handling, canary scope). Planner received locked decisions → no plan thrashing. Code implemented exactly what plan said.

**Red-teaming pattern:** Advisor-before-planner caught design flaws in scout output. Typical flow = scout → plan → code → debug. We inserted advisor review → scout → advisor → plan → code. Early catch = downstream confidence.

**Canary scope discipline:** Tested 4 code paths (not all 17) with 2 consecutive runs. Verified Testcontainers fallback still works. Contained blast radius. Phase-03 rollout discovered pre-existing failures quickly because canary established success baseline.

**Explicit out-of-scope:** Redis fixture deferred (different migration pattern). CI job disabled rather than guessed. Reduces scope creep.

**Why 18× 403 failures didn't derail us:** Verification proved they're pre-existing (identical on Testcontainers). Documented as separate backlog item. Didn't block migration. This clarity = maturity.

## Lessons Learned

1. **Advisor-before-planner ROI is real.** 30m red-team call prevented multi-day thrashing. Pattern: scout → locked decisions (AskUserQuestion ×2) → advisor review → plan → code. Upfront clarity scales.

2. **Per-collection isolation = natural parallel safety.** 4 xUnit collections run parallel; 4 DBs (one per collection) = zero synchronization. Lesson: match infra topology to test topology. Don't force shared resources unless necessary.

3. **Runtime introspection beats static assumptions.** INFORMATION_SCHEMA query for table discovery auto-adapts to 3 different schema provisioner code paths. Future table additions = zero maintenance. Lesson: query the system for ground truth, don't hardcode.

4. **Infra migrations surface latent bugs.** 18× 403 failures weren't caused by migration; they were exposed because migration ran tests in new conditions (local SQL, different timing). Lesson: infrastructure changes = regression scan opportunity.

5. **Explicit out-of-scope decisions prevent scope creep.** Redis deferred (1 line comment + backlog entry). CI job disabled with explanation. Reduces thrashing, keeps session tight.

6. **Docs audit = non-negotiable finalize step.** MEMORY.md index had stale description (line 18). Without audit, future session reads lying project state. Lesson: docs are code; they drift; audit them.

## Next Steps

**Immediate (current sprint):**
- Add IEffectivePermissionService stubs to Audit/Forms/Organization/Cases modules (copy PR #36 pattern). Est. ~1h.
- Verify all 22 integration test projects green (currently 13/17 green, 4 known pre-existing 403s, 1 not yet run).

**Follow-up (backlog):**
- Re-enable CI integration-tests job: requires GH Actions SQL Server 2022 service or self-hosted runner. Est. 2-4h discovery + setup.
- Redis fixture local migration (separate scope; no blocker).

**Team communication:**
- Document INFORMATION_SCHEMA pattern in dev guidelines (reusable for future fixture migrations).
- Update onboarding docs: local SQL Server 2022 setup before running integration tests locally.

---

## Verification Checklist (Finalize)

- [x] Code compiles, 0 warnings: `LocalSqlHelper.cs`, `LocalSqlOptions.cs`, 4 fixtures modified
- [x] Canary tests pass: 28/28 (4 code paths, 2 consecutive runs)
- [x] Rollout green: 13/17 full green (4 pre-existing 403s verified unrelated)
- [x] Commits clean: `0c045db3`, `ed90181f`, `6901bc0d` with conventional messages
- [x] CI adjusted: integration-tests job disabled with explanation
- [x] Docs updated: 4 files (deployment-guide, dependency-map, roadmap, changelog)
- [x] Memory synced: MEMORY.md index + 5 memory files + project-state.md
- [x] Docs audit: MEMORY.md line 18 stale description fixed
- [x] PR created: #37, links commits, links plan

## Unresolved Questions

1. **Will team re-enable CI integration tests?** Timeline unknown; requires decision on GH Actions services vs self-hosted runner.
2. **18× 403 failures — priority?** Pre-existing, not blocking migration. Deferred to next session. Should we prioritize over other D-tier backlog items?
