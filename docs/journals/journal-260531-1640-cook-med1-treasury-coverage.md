# Med-1 Treasury Ward Placeholder Coverage — Integration Test Added

**Date**: 2026-05-31 16:40
**Severity**: Medium
**Component**: Organization seeding / OrgTreeSeeder
**Status**: Resolved

## What Happened

Med-1 blocker: OrgTreeSeeder's treasury ward-placeholder generation path (L4 KBNN placeholders from masterdata.Wards via Dapper) had zero automated coverage. The belief that "legacy BtcOrgUnitSeeder treasury tests provided proof" delayed deletion of the old seeder. Audit revealed the legacy tests were in-memory pattern assertions—they never actually invoked the real Dapper path.

This session closed Med-1 by adding real integration coverage, unblocking BtcOrgUnitSeeder deletion.

## The Brutal Truth

We've been trusting a ghost. A test named "treasury" for 3 commits was pure string-convention assertions on hand-built rows—zero DB access, zero filter validation. The real Dapper+IsActive filter path had never been proved in automated tests. This is the kind of coverage debt that compounds: next dev assumes "tested," skips verifying, ships a bug months later when the code path changes.

The fix was trivial (5 lines of test assertions in an already-open file), but the discovery stings—we shipped seeding changes believing a coverage story that wasn't true.

## Technical Details

**New test**: `SeedTree_GeneratesTreasuryWardPlaceholders_FromActiveWards` in `OrgTreeSeederEquivalenceTests.cs`

- Creates 3 uniquely-coded wards (ZZW001/ZZW002 active, ZZW003 inactive) via MasterDataDbContext
- Raw-SQL deactivates ZZW003 post-insert (Ward.Create always sets IsActive=true)
- Runs full BTC seed (maxLevel:4, skipTreasuryWardPlaceholders:false) under throwaway tenant
- Asserts placeholders exist for active wards only (BTC.KBNN.PLACEHOLDER.{code}, L4 under L3 "khu vực")
- Asserts inactive ward excluded from placeholder generation
- Asserts idempotency (2nd run adds zero placeholders)
- finally-block DELETEs wards; test-scoped tenantId isolates placeholders

**Anti-tautology verification** (3 locks):
1. Silent treasury skip → ContainKey assertion fails
2. Filter removal → NotContainKey + count==2 assertions fail
3. Code collision risk mitigated via ZZW* codes distinct from GSO-numeric seeded codes

**Build/Test**: 0W/0E, OrgTreeSeederEquivalenceTests 3/3 (equivalence + BNV + new treasury), code-review APPROVE 6/6 criteria MET.

## What We Tried

Option A (rejected): Change SeedTreasuryWardPlaceholdersAsync from private→internal + add InternalsVisibleTo. User approval pending decision. Rejected because it rewrites test structure unnecessarily; public SeedTreeAsync path covers the same ground.

Option B (accepted): Cover via public `SeedTreeAsync(BoTaiChinh, maxLevel:4, skipTreasuryWardPlaceholders:false)` entry point. Zero production change. Can't reach defensive early-returns (no-connection/no-wards/no-KBNN-parent), judged YAGNI (trivial branches, unreachable in integration test context).

## Root Cause Analysis

Test naming and test execution are decoupled. A test file `BtcOrgUnitSeederL4TreasuryTests` has the *word* "treasury" but the implementation never touches the treasury seeding code. We treated the filename as proof; we should have read the test method body. Cost of verification: 30 seconds. Cost of undetected drift: every future treasury change becomes a land-mine.

## Lessons Learned

1. **Verify coverage claims before trusting them**. "X is covered by test Y" is itself a claim requiring empirical proof—read what Y actually invokes, not just what its name suggests.

2. **In-memory pattern tests ≠ integration coverage**. Hand-built test data + string assertions can pass while the real code path (Dapper + filters + DB queries) has never executed. Different value (unit-level patterns), but don't conflate with integration proof.

3. **Naming drift is silent**. A test renamed from "AssertWardPlaceholderNaming" to "BtcOrgUnitSeederL4Treasury" without code change can fool audits for months. Periodic "does this test actually call the code it claims?" sweeps are cheap insurance.

## Next Steps

1. Delete BtcOrgUnitSeeder.* orchestration (keep JSON resources + JsonSeedDataLoader + BtcSeedHelpers for reuse) — now gated on review only (treasury coverage satisfied)
2. Swap registration in InfrastructureRegistration.cs to use OrgTreeSeeder exclusively
3. P2.3 — generalize identity seeder (roles/users) + assignment seeder for both tenants
4. dependency-map.md update deferred to legacy-deletion swap commit

**Status**: RESOLVED — commit `c15ee17c` pushed origin+bitbucket (TEST-ONLY)

