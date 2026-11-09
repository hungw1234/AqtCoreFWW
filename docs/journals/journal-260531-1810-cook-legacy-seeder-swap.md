# Legacy BtcOrgUnitSeeder Deleted + OrgTreeSeeder Live Swap (P2.2b)

**Date**: 2026-05-31 18:10
**Severity**: Medium
**Component**: Organization seeding (Infrastructure), Identity seeding (tenant rows)
**Status**: Resolved

## What Happened

Completed the live seeder-swap: deleted legacy BtcOrgUnitSeeder (7 files, 8 tests) and wired OrgTreeSeeder as the single production org-seeding path. Commit `744623ff` (16 deletions, 14 modifications, -2371 lines) pushed to origin + bitbucket. The change forces all-or-both tenant seeding in dev (both BTC + BNV) because OrgTreeSeeder loops TenantSeedProfiles.All with no per-tenant gate.

## The Brutal Truth

Swapping a legacy seeder looks mechanical (delete + wire new) but exposed a latent product decision: "what does a fresh dev DB seed by default?" The parallel-change design (P2.1/P2.2) is all-or-both with no per-tenant gating. Three options existed:

1. **Option A (rejected):** Keep BTC-only parity in dev by adding per-tenant gates to OrgTreeSeeder.
2. **Option B (CHOSEN):** Accept both tenants; enable TenantSeeder + OrgTreeSeeder by default in appsettings.Development.
3. **Option C (rejected):** Defer the swap, keep legacy BtcOrgUnitSeeder alongside new code.

User chose Option B, but it revealed a chicken-egg risk that almost broke fresh-DB startup.

## Technical Details

**The chicken-egg (nearly fatal):**
- TenantBackfillSeeder derives BTC tenant row FROM existing OrgUnits (reactive, pass-2 only).
- Legacy BtcOrgUnitSeeder seeded org units UNCONDITIONALLY (no tenant-row FK; no existence guard).
- A naive tenant-existence guard in OrgTreeSeeder would deadlock fresh dev DB: no tenant row → skip org → backfill never sees org → never creates tenant.

**Root cause:** The reactive backfill was designed to reconcile existing legacy data, not bootstrap new seeding. Proactive TenantSeeder (P2.1, already shipped) solved it by creating both tenant rows BEFORE OrgTreeSeeder runs (Identity module registers before Organization). Verified by checking TenantBackfillSeeder source and module load order.

**What shipped:**
- OrgTreeSeeder.cs: tenant-existence guard via cross-schema Dapper read (`SELECT TOP 1 1 FROM [identity].[Tenants] WHERE PublicId=@id`), skips-with-warning (no throw, no orphan units) if absent; unconditional completion log for reset-all poll marker; TenantRowExistsAsync helper.
- OrgRegistration.cs: removed BtcOrgUnitSeeder registration + BtcSeedOptions bind.
- Deleted: 7 BtcOrgUnitSeeder*.cs partials, BtcSeedOptions.cs, 8 tests.
- appsettings.Development.json: Organization.OrgTreeSeed(Enabled=true) + Identity.TenantSeed(Enabled=true); appsettings.json base: OrgTreeSeed(Enabled=false) prod-safe.
- reset-all.ps1: marker swap ($MK_ORGUNIT → "[OrgTreeSeeder] All tenants complete"); env override Organization__OrgTreeSeed__Enabled=true.
- Tests: SeederMarkerContractTests (typeof anchor), CrossSchemaSqlScanner (whitelisted new identity.Tenants read in OrgTreeSeeder), SeederSmokeFixture (config swap), OrgUnitsControllerSmokeTests (rewrote from BtcOrgUnitSeeder to static OrgTreeSeeder.SeedTreeAsync).

**Kept (shared):** BtcSeedHelpers, BtcSeederTenantContext, BtcSeedConstants, JsonSeedDataLoader, OrgUnitSeedDto, JSON resources.

## What We Tried

1. ✅ Reviewed backfill logic (TenantBackfillSeeder.cs line-by-line) — confirmed it reads FROM org, not standalone.
2. ✅ Checked module registration order (Identity before Organization) — confirmed TenantSeeder runs first.
3. ✅ Ran full startup smoke test (boots host with both seeders enabled, /health/live=200) — the real regression gate.
4. ✅ Whitelisted cross-schema read in architecture guard (new identity.Tenants read in OrgTreeSeeder).
5. ❌ Did NOT attempt per-tenant gates in OrgTreeSeeder (rejected: no gate infrastructure in new design, would defer entire swap).

## Root Cause Analysis

Three root issues:

1. **Latent product decision:** The all-or-both design (TenantSeedProfiles.All loop, no per-tenant gate) was sound for simplifying P2 seeding, but swapping it live forces the dev-startup question upfront. Should have surfaced this earlier.
2. **Reactive-by-design vs. proactive-on-startup mismatch:** The legacy backfill was designed to reconcile, not bootstrap. Swapping to always-on seeding exposes the gap. The proactive TenantSeeder fixes it, but the dependency is implicit (module load order).
3. **Non-obvious blast-radius:** 40 references to BtcOrgUnitSeeder in the repo, but only THREE were code-breaking (typeof anchor, smoke fixture config, one rewrite); the rest were comments/logs/deleted-tests. The ONE blindsiding change: the new cross-schema read MUST be whitelisted or architecture guard fails.

## Lessons Learned

1. **Deleting a replaced component forces product decisions.** Don't hide behind "it's just a swap." Surface the all-or-both seeding choice to the user, enumerate options, make it explicit.
2. **Verify reactive dependencies before adding guards.** The backfill reads FROM org, not standalone. Adding a tenant-existence guard without confirming the backfill would have deadlocked fresh-DB. Always trace the actual source, not assumptions.
3. **Build + unit tests insufficient for startup-path changes.** The active startup smoke test (boots host, /health/live=200, seeding enabled) is the real regression gate. Unit tests pass; startup fails silently.
4. **Cross-schema reads in architectural guards need whitelisting.** New identity.Tenants read in OrgTreeSeeder.cs wasn't obvious because the seeder is in Organization module, not Identity. Whitelist update required.

## Next Steps

1. **P2.3 (carry-fwd):** Generalize identity seeder (BtcIdentitySeeder.* roles/users/userroles) + assignment seeder for both tenants, then delete legacy BtcIdentitySeeder.* + BtcAssignmentSeeder — the last P2 slice.
2. **Full runtime verification (user-run):** reset-all.ps1 full execution with both tenants, verify /health/live=200, poll markers hit, DB state correct. (P1 was script-only; P2 = live run by user.)
3. **Existing dev DBs:** Keep their backfilled BTC tenant row with ugly `TENANT-{shortId}` code until reset (TenantSeeder skips by PublicId, does not reconcile).

**Verification:** Build 0W/0E; tests 21/21 (incl active startup smoke 5/5); architecture guard green; code review 8/8 criteria MET, 0 Critical/High.

---

**Status:** RESOLVED
**Summary:** Swapped legacy BtcOrgUnitSeeder to OrgTreeSeeder live path (commit `744623ff`), forced decision on all-or-both tenant seeding in dev, verified chicken-egg deadlock-free via proactive TenantSeeder. Startup smoke test is the real regression gate.
