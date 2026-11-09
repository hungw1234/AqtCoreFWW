# Journal — 2026-05-31 14:20 · `/cook P2.2a` OrgTreeSeeder Parallel-Change (Org Generalization)

## Session goal
P2.2a = generalize legacy `BtcOrgUnitSeeder` into tenant-agnostic `OrgTreeSeeder` + equivalence test, **parallel-change discipline** (new code gated-OFF, old untouched). Ship + push both slices (P2.1 TenantSeeder + P2.2a OrgTreeSeeder). No deletion this session.

## What shipped

**Commit `11110d9c` (P2.2a) + `861e2714` (P2.1, prior) — both pushed origin+bitbucket:**

### 5 new seeder files (tenant-agnostic data-driven)
- **`OrgTreePlan.cs`** — record: RootCode + JsonResources + GenerateTreasuryWardPlaceholders. Encodes seeding shape.
- **`OrgTreePlans.cs`** — keyed-by-code registry, BoTaiChinh plan w/ 9 JSON resources (level-1 depts → level-4 statistics teams + treasury wards). TryGet→null for undefined codes (BNV gracefully defers).
- **`OrgTreeSeederOptions.cs`** — `Organization:OrgTreeSeed` (config section), Enabled default false, MaxLevel global ceiling.
- **`OrgTreeSeeder.cs`** — IHostedService + IIntegrationTestSeeder; StartAsync loops TenantSeedProfiles.All → resolve plan per-tenant → SeedTreeAsync. Data-driven level-ascending per-code-parent, idempotent by (TenantId, Code), `Where(l>=1 && l<=maxLevel)` filters out level-0 metadata sentinel.
- **`OrgTreeSeeder.Treasury.cs`** — verbatim Dapper port of legacy L4 KBNN ward-placeholder generation via masterdata.Wards, plan-gated.

### EDIT `OrgRegistration.cs`
Gated registration after BtcOrgUnitSeeder (parallel-change, OFF by default).

### NEW test `OrgTreeSeederEquivalenceTests.cs`
1 PASS real-SQL: L1=1, L2=35, L3=109, L4 tax `BTC.C.THUE.`=350, L4 stats `BTC.C.TK.`≥476, `_META` non-pollution, idempotent re-seed. **MarkerContractTests** 2/2 PASS (both seeders target same tenantId=`a1b2c3d4-…-b7c1`).

---

## What didn't / lessons

### (260531-A) Stale prefix — test caught a real bug
Explore agent's prefix info was outdated: actual L4 statistics prefix = `BTC.C.TK.` NOT `BTC.C.THONGKE.`. First equivalence run FAILED on count mismatch → discovered root cause: wrong prefix in docstring → fixed seeder + added `_META` non-pollution assertion → PASS. **Lesson:** non-vacuous equivalence test must assert concrete counts/prefixes, not just "rows exist." Live-SQL equivalence proved accuracy over a stale documentation source.

### (260531-B) Treasury coverage gap — blocker for legacy deletion
Treasury Dapper path (`OrgTreeSeeder.Treasury.cs`) has ZERO automated coverage. Equivalence test runs with `skipTreasuryWardPlaceholders:true`; only legacy `BtcOrgUnitSeeder` tests the treasury branch. **Consequence:** cannot delete legacy seeder until new treasury path covered. Faithful port + gated-OFF mitigate risk, but this is a real **Med-1 blocker** for legacy-deletion swing.

### (260531-C) Implicit interceptor behavior verified
Reviewer manually created `new OrgDbContext(dbOptions, BtcSeederTenantContext.Instance)` in equivalence test. Risk: missing interceptors? Verified: DI interceptors are baked into `dbOptions` at registration time → new instance re-uses them. No hidden dependency on service-locator pattern.

---

## Design decisions

### Parallel-change discipline
- **New code gated-OFF** (`Organization:OrgTreeSeed:Enabled=false` in Dev appsettings).
- **Untouched legacy path** (`BtcOrgUnitSeeder` still runs by default, owns live seeding).
- **Equivalence test proves output-equivalence** — row counts + prefixes match between old/new.
- **Deletion deferred** — after equivalence green + review approval, swap registration + delete legacy orchestration only (JSON resources + helpers + BtcSeedHelpers remain for interim reference).

### Per-tenant MaxLevel binding
Seeder now uses `Math.Min(profile.MaxLevel, _opts.MaxLevel)` — per-tenant depth from `TenantSeedProfiles` (BTC=4, BNV=2), capped by global option. Latent surprise avoided: BNV MaxLevel=2 will now be enforced correctly in P2.2b.

---

## Technical correctness

**Code review findings (0 Critical/High):**
- **Low-1 (applied):** Dropped plan-phase token `P2.2b` from comment — per CLAUDE.md §5, code must not reference plan artifacts.
- **Low-2 (applied):** Per-tenant MaxLevel binding as described above.
- **Med-1 (carry-fwd, not fixed):** Treasury coverage gap (see 260531-B).

**Verification (code-reviewer ran locally):**
- Build 0W/0E.
- Equivalence test 1/1 PASS (real SQL via testcontainers).
- Marker contract 2/2 PASS.
- Grep-verified: `BtcSeedConstants.TenantId` == `TenantSeedProfiles.BoTaiChinh.TenantId` — both seeders target identical (tenantId, code) rows → enabling both = per-code idempotent (no double-seed). Equivalence test doesn't exercise overlap directly (uses random Guid), but grep settles the invariant.

---

## Carry-forward

**Next slice (P2.2b):**
- Seed BNV 22-unit tree (`bnv-level-1-2.json` + BNV entry in `OrgTreePlans`), generated GUIDs, no E2E hardcoding.
- MaxLevel=2 per profile.

**Legacy seeder deletion (P2.2c):**
- Swap registration: remove `BtcOrgUnitSeeder`, enable `OrgTreeSeeder`.
- Delete `BtcOrgUnitSeeder` orchestration files.
- Keep JSON resources + JsonSeedDataLoader + BtcSeedHelpers for reference.
- **BLOCKER:** Treasury coverage (Med-1) must land first.

**P2.3 identity generalization:**
- Generalize `BtcIdentitySeeder` + `BtcAssignmentSeeder` (roles/users/assignments).
- Delete legacy stubs.

**Dependency-map.md update:** Deferred to legacy-deletion swap (live path unchanged until then).

**Remote state:** Oracle remote diverged +504 commits (non-blocking; origin/bitbucket canonical).

---

## Key meta-lessons

1. **Equivalence test discipline** — Test output-equivalence to legacy BEFORE deleting. Test must be non-vacuous (assert concrete counts + business-meaningful prefixes, not just "rows exist").
2. **Parallel-change reduces risk** — New code OFF-by-default, old runs unchanged, test proves equivalence → deletion is low-stakes. Orthogonal to integration (no overlap test required; grep settles it).
3. **Live SQL > doc** — Stale docstring on prefix went unnoticed until equivalence ran against real seeded data. Live test is the ground truth.
4. **Treasury coverage gap is real** — Faithful port mitigates risk, but omitting automated tests = you cannot delete the reference. Mark as Med-1 blocker up-front.

---

## Status
**DONE**

Commit `11110d9c` + P2.1 `861e2714` both shipped to origin+bitbucket. Working tree clean. 5 files staged+pushed. Build 0E, test 1/1, review 0 Crit/High. Ready for P2.2b handoff.

