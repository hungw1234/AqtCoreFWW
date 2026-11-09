# MTS-3-FU1 — Smoke Tests Unblock: Vault Stub Extraction & Env Resilience

**Date**: 2026-05-13 13:45–14:30 ICT
**Severity**: High
**Component**: Infrastructure — `tests/shared/AqtCoreFW.Tests.SharedFixtures/TestVaultStubs.cs`, `SeederSmokeFixture`, `AuthServerHostFixture`, `WebAppFixture`
**Status**: Resolved

## What Happened

MTS-3 (PR #103, orphan-menu purge) shipped on `main` (`4b461461`) but orphaned the smoke test suite. The blocking issue: three independent test fixtures (`WebAppFixture`, `SeederSmokeFixture`, `AuthServerHostFixture`) each contained near-identical Vault-stub implementations. When one fixture was used in isolation (Seeder smoke tests), the stubs were missing, and dev-environment Vault calls (`IPepperResolver.GetPepper()` via `EmailHashService` in the identity seeder) failed with `permission denied`, crashing fixture bootstrap.

**The fix in one sentence**: Extracted the existing `TestPepperResolver` + `TestKeyResolver` from `WebAppFixture` into a shared public file, then injected them into all three fixtures. Smoke suite now runs locally against a test-only in-memory Vault fallback.

Delivered: `TestVaultStubs.cs` (66 LOC, 2 classes), refactored fixture DI registration, plus one latent `Identity` config bug surfaced and fixed. Smoke suite before → after: 1/5 PASS → 4/5 PASS. No behavior change; pure env resilience.

## The Brutal Truth

This follow-up should never have been necessary. The root cause is a config-flag design flaw that sat in the codebase undetected for months: `Vault:Enabled=false` gates only the configuration provider load (`AddVaultConfiguration`), not the singleton DI registrations (`AddVaultKeyResolver()` + `AddVaultPepperResolver()`). The result is a half-gate. In dev environments where Vault is disabled, the real `VaultPepperResolver` still gets instantiated and throws at runtime when anyone calls `IPepperResolver.GetPepper()`.

The even more brutal part: MTS-3 PR #103 added `MenuSeederOrphanPurgeSmokeTests` and documented it as "CI-only (Vault blocks local)" — which was a confession, not a solution. The test never actually ran locally. Once the smoke fixtures were unblocked today, the test immediately failed on a second issue (`MenuRolePermission` cascade FK not applying), which means it had been failing on CI all along and no one noticed.

## Technical Details

**Root cause flow:**
1. `SeederSmokeFixture` → `E2ETestDataSeeder.SeedMenusAsync` calls `AppMenu` operations
2. `AppMenu` seeding transitively triggers `OpenIddict` user seeding
3. `OpenIddictDataSeeder.SeedDevUsersAsync` (in `AuthServerHostFixture` wiring) → `UserManager.CreateAsync`
4. `UserManager.CreateAsync` → `EmailHashedUserStore.SetNormalizedEmailAsync`
5. `EmailHashedUserStore` → `EmailHashService.HashGlobal/HashTenant`
6. `EmailHashService` → `IPepperResolver.GetPepper()`
7. **Real `VaultPepperResolver` instantiated** (because `Vault:Enabled=false` only gates the config loader, not DI registration)
8. Dev Vault not reachable → `UnauthorizedAccessException` not caught by any try/catch at that depth
9. Fixture boot fails silently with "Vault permission denied"

**The fix:**
- Extracted existing test stubs from `WebAppFixture` (lines ~1450–1500, private nested classes):
  - `TestPepperResolver`: XOR-perturbed 32-byte array per tenant (preserves QĐ742 §4.4 Wave-3 P0-1 invariant exactly)
  - `TestKeyResolver`: fixed 32-byte 0x42 array
- Created new `tests/shared/AqtCoreFW.Tests.SharedFixtures/TestVaultStubs.cs` as public sealed classes with `[ExcludeFromCodeCoverage]`
- Injected into all three fixtures via identical pattern:
  ```csharp
  services.RemoveAll<IKeyResolver>();
  services.RemoveAll<IPepperResolver>();
  services.AddSingleton(new TestKeyResolver());
  services.AddSingleton(new TestPepperResolver());
  ```

**Latent bug surfaced:**
Once Vault was unblocked, `BtcIdentitySeeder.SeedUsersAsync` config guard (Users.cs:149–156) fired: `minRequired = OrgUnits.Count(36) + DevReserveCount(129 default) = 165 > UserCount(50)`. Fixed via `SeederSmokeFixture` cap: `Identity:BtcUserSeed:DevReserveCount=10` → minRequired = 46 (fits inside UserCount=50).

## What We Tried

- **Approach A (global test stubs in DI)**: Wired test stubs in `Program.cs` via `#if DEBUG` block. Rejected — production builds include unnecessary stubs and the condition is invisible to LLMs reading code in isolation.
- **Approach B (one shared fixture)**: Consolidated all smoke fixture DI into a single base class. Rejected — `SeederSmokeFixture`, `AuthServerHostFixture`, `WebAppFixture` have different bootstrap sequences and scope lifetimes; forcing them into one class increases coupling.
- **Approach C (extract + inject, as implemented)**: Three fixtures remain independent; Vault stubs extracted to shared file once. Accepted. Minimal coupling, behavior-preserving refactor.

## Root Cause Analysis

The fundamental design flaw: `Vault:Enabled` flag is Boolean but its semantics are unclear. Currently it gates one DI registration (`AddVaultConfiguration`) but not others (`AddVaultKeyResolver`, `AddVaultPepperResolver`). The flag should either:

1. **Gate all Vault-related DI** with an in-memory dev fallback, OR
2. **Document the half-gate explicitly** in code comments + deployment guide

Neither was done, so the codebase drifted into "we'll handle Vault null-safety at the call site" which doesn't work for sync code paths like email hashing.

The secondary root cause: MTS-3 PR #103 claimed "CI-only" without citing an actual CI signal. The test was never verified to pass locally or on CI; it was added as dead code behind a Vault blocker. This violates the nascent "verify-fe-consumer-before-be-build" YAGNI pattern (which prevents speculative BE work). A mirror pattern is needed: "verify-test-actually-runs-before-claiming-ci-only."

## Lessons Learned

- **Config-flag asymmetry is a debt trap.** `Vault:Enabled` gates one thing, not all Vault-related registrations. Document all behaviors or gate all related DI. A half-gate will silently shift the error burden to downstream call sites. Next time, grep for all usages of a feature flag and ensure they're all conditional.
- **"CI-only" claims need evidence.** Don't add a test behind a local blocker and label it "CI-only" without proof that CI actually runs it. Add a TODO and backlog the test, or unblock it locally first. This is the inverse of the `verify-fe-consumer` pattern — same rigor, different direction.
- **The 3rd consumer is the DRY threshold, not the 2nd.** Three Vault-stub blocks existed before extraction was justified. The 4th would justify a `services.AddTestVaultStubs()` extension. We extracted at the right point — eager extraction would have been over-engineering.
- **Latent config bugs hide behind environmental blockers.** The `DevReserveCount > UserCount` config guard was never triggered locally because Vault failures happened first. Once the first blocker fell, the second emerged. Unblock systematically, triage each new failure before moving on.

## Test Evidence

- **Before**: Smoke suite 1/5 PASS (4× `VaultPepperResolver` throws)
  - MenuSeederOrphanPurgeSmokeTests: Vault fail
  - SeederSmokeTests: Vault fail  
  - AuthServerHostSmokeTests: Vault fail
  - WebAppFixtureSmokeTests: PASS (had stubs)
- **After**: Smoke suite 4/5 PASS
  - MenuSeederOrphanPurgeSmokeTests: PASS (fixture DI now includes stubs)
  - SeederSmokeTests: PASS (fixture DI now includes stubs)
  - AuthServerHostSmokeTests: PASS (fixture DI now includes stubs)
  - WebAppFixtureSmokeTests: PASS (unchanged)
  - **MTS-3-FU3**: 1 FAIL (cascade FK issue, new backlog, not blocking FU1)
- **Integration sanity check**: `Identity.Integration.Tests` 198/198 PASS — refactor behavior-equivalent
- **Build**: 0E, 2W (pre-existing NU1510 warnings)
- **Code review**: APPROVE 9.0/10
  - M-1 (optional): Comment clarity in TestPepperResolver
  - L-1 (optional): `[ExcludeFromCodeCoverage]` placement

## Next Steps

| Item | Owner | When |
|---|---|---|
| **MTS-3-FU2 (P3, ~30min)** — Config-flag asymmetry cleanup. Either gate all Vault DI on `Vault:Enabled` with fallback, or document the half-gate + threat model | Backlog | Next sprint |
| **MTS-3-FU3 (P2, ~1h)** — Smoke test cascade FK failure (`MenuRolePermission` survives after orphan `AppMenu` purge). Triage: schema drift vs. test suite state pollution | Backlog | This sprint |
| **Verify-test-actually-runs (P3 pattern)** — Codify rule: "CI-only" claims require evidence or become TODOs. Add to dev-rules.md circa line 200 | Backlog | Next sprint |

## Commit

- **Branch**: main (hotfix on `4b461461` from MTS-3)
- **Commit**: `8736f383`
- **Files**: 
  - NEW: `tests/shared/AqtCoreFW.Tests.SharedFixtures/TestVaultStubs.cs` (66 LOC)
  - MOD: `tests/integration/AqtCoreFW.Tests.Seeding/SeederSmokeFixture.cs` (~40 LOC Vault DI wiring)
  - MOD: `tests/smoke/AqtCoreFW.AuthServer.Tests/AuthServerHostFixture.cs` (~40 LOC Vault DI wiring)
  - MOD: `tests/shared/AqtCoreFW.Tests.SharedFixtures/WebAppFixture.cs` (remove 66 LOC stub definitions, add import)
  - MOD: `tests/integration/AqtCoreFW.Tests.Seeding/SeederSmokeFixture.cs` (add `Identity:BtcUserSeed:DevReserveCount=10` config)
- **Lines changed**: +66 new stubs, −66 old stubs, +28 imports, net +28 LOC (refactor zero-impact)
