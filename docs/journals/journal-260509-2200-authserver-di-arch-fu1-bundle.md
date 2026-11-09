# AuthServer DI Arch FU1+FU2+FU3 Bundle Shipped

**Date**: 2026-05-09 22:00
**Severity**: Low
**Component**: Infrastructure / AuthServer + Api Program initialization
**Status**: Resolved

## What Happened

3 follow-up tasks (P3) from AuthServer-DI-Arch ship (PR #95 from 2026-05-08) bundled into single session. User chose production-touch strategy (Path A: wrap Program classes in namespaces) over my recommendation (Path B: extern alias plumbing). Shipped ~3h; commits `6f0b4412`, `063bc0c6`, `98b0fcb9` on main.

**Scope**: 2 production files edited (Api + AuthServer Program.cs), 11 test files (4 new + 7 relocated/refactored). Tests: Smoke 4/4, AuthServer.Tests 27/27, Architecture 33/33.

## The Brutal Truth

User explicitly owned the trade-off: larger blast radius for long-term cleaner production state. This directly inverted the recent `verify-fe-consumer-before-be-build` YAGNI pattern (which said "skip speculative BE-only work"). Both strategies are contextually correct—difference is: YAGNI pattern favors 25-35× ROI source-verify-then-skip for *speculative* work; Path A here was *blocking* (Program class collision prevents ApiHostFixture + AuthServerHostFixture coexistence in shared test suite). User prioritized production cleanup over test-side plumbing, and that's legitimate.

Frustrating part: discovered 3 nasty test-surface gotchas *during* Phase 5 that would have shipped broken if not caught. Seeder shared-DB collision, VNeId prod-guard, env-var race. Each surfaced with clean isolation because of phased execution, but if batched, diagnosis would have been much messier.

## Technical Details

### Production Changes
- **Api/Program.cs**: Wrapped `Program` class in `AqtCoreFW.Api.Host` namespace, converted implicit Main to explicit `public static async Task Main`. 478→483 LOC.
- **AuthServer/Program.cs**: Same pattern, `AqtCoreFW.AuthServer.Host` namespace. 384→390 LOC.
- Unblocks ProjectReference coexistence: both hosts now safe as test dependencies (no type collision on `Program`).

### Test Infrastructure (5 Surfaces)

**Phase 1 (Api smoke)**: New `ApiHostFixture` mirroring `AuthServerHostFixture` design—private Testcontainer, strips `IHostedService`, runs `Development` env (not `Production`, which would trigger VNeIdHttpConnector "register before deploying" guard).

**Phase 2 (AuthServer refactor)**: Relocated `AuthServerHostFixture` + `AuthServerStartupValidationTests` from unit→integration project (FU2 requirement). Tests 29→27 (2 facts moved to smoke suite).

**Phase 3 (architecture guard)**: Extracted `HostDiArchGuard` static helper shared between both smoke test classes. Lives in SmokeTests project (not /Shared/, due to ProjectReference coupling constraint).

**Phase 5, Surface 1 — Seeder Collision**: Initial design bundled 2 smoke facts into single test class. Failed on 2nd fact: `BtcIdentitySeeder` expects `UserCount < 50` fresh, but SqlServerFixture (collection-shared) left 36 OrgUnits from test 1 → test 2 validation tripped. **Fix**: New `ApiHostFixture` with own private container. Seeder never runs (IHostedService stripped).

**Phase 5, Surface 2 — Prod-Only Guard**: `ApiHostFixture` tried `Production` env to skip seeders; hit "Register a production IVneIdConnector before deploying" exception. **Fix**: Switched to `Development` (still skips seeders, plus `ValidateOnBuild=true` gives extra DI validation).

**Phase 5, Surface 3 — Env-Var Race**: Parallel test execution caused collision on `ConnectionStrings__Default`. Both `SeederSmokeFixture` and `ApiHostFixture` write during Initialize/Dispose. **Fix**: Assigned all smoke tests to same `[Collection(SmokeTestCollection.CollectionName)]` to serialize.

## What We Tried

1. Initial: Single smoke test class with 2 facts (Api + AuthServer). Failed Phase 5 Surface 1 (seeder collision). Refactored to separate fixtures.
2. Initial: Production env to skip seeders. Failed Phase 5 Surface 2 (VNeId guard). Switched to Development.
3. Initial: Parallel test execution. Failed Phase 5 Surface 3 (env-var race). Added collection-level serialization.

All paths cleared once surfaces isolated; no false-green patterns.

## Root Cause Analysis

**Why surfaces weren't caught earlier**: Test infra design (SharedFixtures→BtcIdentitySeeder→SqlServerFixture coupling, VNeId production-only validation, env-var mutation during test init/cleanup) only manifests under actual test execution. Source-review caught Program class collision early, but test-side gotchas required runtime.

**Why Path A chosen over Path B**: User wanted long-term production state cleanup, not temporary plumbing. Legitimate trade-off. Path B (extern alias) would be zero-touch, lower risk, but leaves Program class collision dormant for next developer.

## Lessons Learned

1. **Source-verify scope BEFORE planning still core**—caught Program collision + ValidateOnBuild + seeder coupling before plan. Spend 10 min reading state; save 2h+ diagnosis.

2. **Path A vs Path B: user owns trade-off**—YAGNI pattern (verify-skip) is *speculative-work* optimization; blocked work (Program collision) is different context. Both valid. Present both clearly, don't bias.

3. **Phased test execution per GATE worked even when GATEs passed**—Phase 1 + 2 clean, but Phase 5 phase-structure isolated test-infra failures from production refactor. When 3 surfaces hit (seeder, prod-guard, env-var), knew production was solid.

4. **Per-phase test runs > batch verification**—Each surface surfaced with isolated stack trace. Batched run would cascade; hard to diagnose in parallel.

## Next Steps

1. **Memory sync**: Create `authserver-di-arch-fu1-fu2-fu3-bundle-shipped-260509.md` (project record). Create `path-a-vs-b-trade-off-source-touch-vs-extern-alias.md` (feedback record for future context).
2. **No code follow-up**—all surfaces resolved. No regressions, no backlog spawned. Verify negative test fires if DeadLetterService removed (already confirmed).

---

**Status**: DONE
**Summary**: AuthServer DI arch FU1/FU2/FU3 bundle shipped clean. 3 test-surface gotchas (seeder collision, prod-guard, env-var race) surfaced + fixed via phased execution. User chose production cleanup (Path A) over plumbing (Path B)—legitimate trade-off, not YAGNI override.
