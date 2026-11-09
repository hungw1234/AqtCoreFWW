# Test 403 Stubs: Audit + Forms + Org + Cases

**Date**: 2026-04-18 08:22–08:50
**Severity**: Low (test infrastructure)
**Component**: Integration test stubs (Identity.Domain permission codes)
**Status**: Resolved — PR #38 merged
**Commits**: `73295ce3` (impl), `73f0d2fc` (docs v2.115)

## What Happened

Merged PR #37 (Testcontainers→local SQL, ~9 AM). During Phase-03 rollout, scout had flagged 18 pre-existing 403 permission failures across Audit, Forms, Organization, and Cases integration tests. User decision: fix in separate PR (#38) before proceeding to Phase-04 (workflow cases integration).

Created branch `fix/test-403-stubs-audit-forms-org-cases` from main (`9fc3058b`). Identified 4 target test files: RtbfEndpointsTests, FormEndpointsTests, OrgUnitEndpointsTests, CaseEndpointsTests. Pattern sourced from PR #36 (NotificationEndpointsTests.cs:245–278): add `CreateClientWithPermissions` helper to each test file, replace 18+1 failing test calls with permission-aware HTTP clients.

Impl completed: copied 27-line helper × 4 files, replaced failing test setups with perm-aware clients keyed to module-specific permission codes (read from live controllers). Build clean (0 errors). All 33/33 target tests passing on local SQL Server 2022.

## The Brutal Truth

Expected 18 failures, found 19. Forms lifecycle user client needed both Submit.Create and Submit.View (not initially counted in scout pass). Minor friction, not a blocker — scout surfaces known-failing tests, impl discovers gaps at runtime. Acceptable variance for pre-estimate.

Also discovered pre-existing bug in CaseEndpointsTests JSON parse (data.data → data.items, likely test data shape mismatch introduced in Phase-03). Fixed as drive-by (1 line, scope already open). YAGNI concern on scope creep — mitigated because cost was ~0 and deferred tech debt payoff.

Session stayed tight: 30 min wall clock (vs 1 hour planned) because pattern precedent was recent (PR #36 only 4 days old) and scope was precise (4 files, no API changes).

## Technical Details

**Target files modified:**
- `src/Modules/Audit/Tests/Audit.IntegrationTests/RtbfEndpointsTests.cs` (added 27-line helper, 8 tests now use CreateClientWithPermissions)
- `src/Modules/Forms/Tests/Forms.IntegrationTests/FormEndpointsTests.cs` (7 tests fixed + 1 lifecycle user client added)
- `src/Modules/Organization/Tests/Org.IntegrationTests/OrgUnitEndpointsTests.cs` (8 tests fixed)
- `src/Modules/Cases/Tests/Cases.IntegrationTests/CaseEndpointsTests.cs` (10 tests fixed + 1 JSON parse bug fixed)

**Permission code mapping (from live controllers):**
- Audit: `PermCodes.Audit.Read.Rtbf`
- Forms: `PermCodes.Forms.Submit.Create`, `PermCodes.Forms.Submit.View`, `PermCodes.Forms.Template.View`
- Org: `PermCodes.Organization.Read`, `PermCodes.Organization.OrgUnit.View`, etc.
- Cases: `PermCodes.Cases.Read`, `PermCodes.Cases.View`, etc.

**Test run results (local SQL):**
```
Audit:     8/8 passing
Forms:     7/7 passing (was 5/7 pre-fix)
Org:       8/8 passing
Cases:    10/10 passing (was 9/10 pre-fix, JSON parse)
━━━━━━━━━━━━━━━━━━━━━━━
Total:    33/33 passing
```

**Csproj updates:**
Added Identity.Application + Identity.Domain project references to test projects (required to reference PermCodes and helper).

## What We Tried

1. **Scout pattern from PR #36**: NotificationEndpointsTests.cs showed exact pattern. Applied wholesale to 4 test modules. No iteration needed.
2. **Grepped controllers for permission codes**: Extracted actual codes from live endpoint auth attributes. No guessing — source of truth.
3. **Local SQL run + verify**: All 33 tests green. Confidence high.

## Root Cause Analysis

Scope came pre-identified by scout (Phase-03 rollout). Cause of 403s: integration tests call endpoints without permission claims in HttpClient. Solution: `CreateClientWithPermissions(...)` helper per test file, mirroring PR #36 pattern (already validated by notif tests).

Why separate PR? User decision: PR #37 already large (Testcontainers→SQL migration). Splitting #38 keeps review clean + allows rapid iteration on stubbed perms independent of infra work.

Gap of 1 (19 vs 18 expected): Forms lifecycle test had dual HttpClient pattern (admin setup + user execution). Scout counted 5 Forms failures (3 auth + 2 lifecycle), but reported "18 total" (lower count, unclear rollup). Impl discovered true scope = 19. Non-critical: count variance acceptable in pre-estimates.

## Lessons Learned

1. **Precedent > new abstraction.** Copy-paste 27 LOC × 4 = 108 LOC. Matches NotificationEndpointsTests + WorkflowTaxonomyAdminEndpointsTests (private helper pattern). Extract to WebAppFixture would expand fixture scope + break convention. YAGNI gate: proceed only when 5th copy needed.

2. **Drive-bys during open scope.** Found JSON parse bug (data.data → data.items) in CaseEndpointsTests. Pre-existing, unrelated to 403 fix. Cost: 1 line, scope already in hand. Accept drive-bys when cost ≈ 0 + deferred tech debt repay. Reject if scope creep risk > 5 min.

3. **Scout estimates drift at impl time.** Scout: 18 failures. Impl: 19. Variance of 1 acceptable. Lesson: scout surfaces known-failing patterns, impl validates real scope. Trust impl-time discovery > pre-estimate if counts matter. Flag variance for memory (done).

4. **Simple scope = fast session.** Pattern known (PR #36 recent), scope precise (4 files), no API changes. Session 30 min vs 60 min planned. Orientation investment (~5 min grep + read) paid off.

## Next Steps

- **None blocking.** All flagged backlog items from PR #37 now closed. Phase-04 (workflow cases integration) unblocked.
- **Optional (YAGNI):** extract `CreateClientWithPermissions` to WebAppFixture if pattern spreads beyond 4 tests.
- **Session state:** Clean. Main @ `73f0d2fc` (PR #38 merged).

---

**Files changed:** 4 test files + 4 csproj refs + docs/roadmap + docs/changelog + backlog.md
**Loc added:** ~108 (27 × 4) helper LOC; 1 bug fix
**Test coverage:** 33/33 target tests passing (18 prev failing → now green; 1 prev parse bug → fixed)
