# P2.2b — BNV Org Tree Data Seeding (Generics Payoff)

**Date**: 2026-05-31 15:30
**Severity**: Low
**Component**: Organization.Infrastructure / Seeding
**Status**: Resolved

## What Happened

Shipped Phase 2.2b: seeded the Bộ Nội Vụ (BNV) org tree as pure data atop the tenant-agnostic OrgTreeSeeder from P2.2a. Proved the seeder abstraction is genuinely generic — adding a second tenant's org structure cost only 3 files (JSON resource, plan entry, test) with zero seeder-logic changes.

## The Brutal Truth

This session was the test. P2.2a refactored hard to make seeding logic tenant-agnostic (generic MaxLevel, configurable treasury placeholders, pluggable ByCode plans). Looked elegant on paper. P2.2b proved it actually works: I didn't touch a single seeder method — I just dropped a JSON file, registered the plan, and wrote a new test. No code path duplication. That's the definition of "generic that actually generalizes."

Dangerous moment: during review, caught myself second-guessing the sentence-case names ("Báo Dân trí" vs title-case in source). Stopped. The test asserts exact strings; they stay in sync with code. That's the invariant. Diacritics round-trip clean — no mojibake, UTF-8 BOM-less. Verified.

## Technical Details

**Shipped (commit `cb32b833`, origin+bitbucket):**

1. **`bnv-level-1-2.json`** — 23 nodes:
   - L1: 1 root (`BNV`, `parentCode: null`)
   - L2: 22 units (10 Vụ `BNV.V.*`, 5 Cục `BNV.C.*`, 3 departments `BNV.TT`/`BNV.VP`/`BNV.B.TDKT`, 4 business units `BNV.SN.*`)
   - Source: Nghị định 25/2025 (post-merger BNV+LĐTBXH), Giai-đoạn-1
   - Names: full Vietnamese diacritics, sentence-case (match BTC style)

2. **`OrgTreePlans.cs`** — added `BoNoiVu` plan:
   - `RootCode: "BNV"`
   - `JsonResources: [bnv-level-1-2.json]`
   - `GenerateTreasuryWardPlaceholders: false` (BNV has no KBNN/L3+)
   - Registered keyed by `TenantSeedProfiles.BoNoiVu.Code`

3. **`OrgTreeSeederEquivalenceTests.cs`** — new test `SeedTree_BoNoiVu_Produces1RootAnd22Level2Units`:
   - Fresh synthetic tenant, runs seeder with OrgTreePlans.BoNoiVu (maxLevel:2, skipTreasury:true)
   - Asserts L1=1 (BNV, parentCode=null), L2=22 (all parent=root), no L3+
   - Diacritic round-trip ("Vụ Tổ chức - Biên chế", "Báo Dân trí")
   - Idempotent (2nd run produces same state)
   - Verifies non-vacuous: hard predicates fail on count/parent/structure if data incorrect

**Verification:**
- Build: 0W, 0E
- Tests: `OrgTreeSeederEquivalenceTests` 2/2 (BTC equivalence still green + new BNV test pass)
- Review: 8/8 criteria met, 0 Critical/High/Medium, reviewer ran full assembly locally, hand-verified 22-unit count against ND-25, confirmed diacritics UTF-8 clean

## What We Tried

Minimal scope. P2.2a was the heavy lift (logic generalization). P2.2b is proof of concept: zero logic changes, only data. Reviewer checked for common diacritic traps (mojibake from BOM-variance, round-trip fidelity). All clean.

## Root Cause Analysis

This wasn't a failure session; it was a validation. P2.2a's parallel-change strategy (decouple data from logic) paid off exactly as designed. The seeder logic knows nothing about BNV, BTC, or treasury structures. It reads a config + JSON file + applies max-level gate. That's how generics should work.

## Lessons Learned

- **Generics are proven by second instance, not first.** P2.2a looked generic. P2.2b confirmed it. The cost of BNV (3 files, zero seeder edits) is the metric of success.
- **Test assertions ground the invariants.** Sentence-case vs title-case doesn't matter; the test asserts exact names. Diacritics don't matter; the test asserts round-trip. That's what ensures sync with reality.
- **Hard predicates catch vacuous tests.** The test would pass even with wrong data if it only checked "tree not null." It checks count=22, parent=root, diacritics exact. That's the difference between "test exists" and "test proves something."

## Next Steps

1. **Delete legacy BTC orchestration** (gated): `BtcOrgUnitSeeder.cs` / `.Treasury.cs` + swap registration. Blocked on Med-1 finding: treasury Dapper integration path needs automated coverage before legacy seeder (currently only treasury test) is removed. Self-contained task ~1d.
2. **P2.3 — generalize identity + assignment seeders** for both tenants. Delete BtcIdentitySeeder/BtcAssignmentSeeder, same pattern (tenant-agnostic logic + plan entries + tests). Estimate 2d.
3. **Defer dependency-map.md update** to the legacy-deletion swap. Live path still BTC-specific until then.

---

**Status**: DONE
**Summary**: P2.2b shipped BNV org tree as pure data on generic seeder. Zero seeder-logic changes, 2/2 tests pass, diacritics verified, carry-fwd gated on treasury test coverage.
