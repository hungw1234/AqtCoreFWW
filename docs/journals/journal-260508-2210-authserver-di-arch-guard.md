# AuthServer DI Architecture Guard: Preventive Ship

**Date:** 2026-05-08 22:10 ICT
**Severity:** Medium
**Component:** AuthServer, Identity.Infrastructure, startup validation
**Status:** Resolved

## What Happened

Merged PR `5d3c36e2` on branch `fix/authserver-di-arch-guard`. Added two interdependent tests + one Testcontainers fixture to validate AuthServer's dependency injection chain at build time. Tests catch transitive dependency gaps that would otherwise surface as 500 errors on first live request. 

**Scope:** 3 new files (~270 LOC total), 2/2 tests, AuthServer.Tests csproj upgraded (+Microsoft.AspNetCore.Mvc.Testing, +Testcontainers.MsSql).

## The Brutal Truth

Recurring pattern on AuthServer Program.cs. Two incidents in prior weeks:
1. **v2.133p3 (Apr 28):** `IMessageBus` missing from DI chain → `/connect/authorize` 500 on first deploy
2. **Today (May 8):** `IGuidGenerator` missing → caught manually during PHASE7-TIER-BC-VALIDATION live test, not by any automated guard

Both were transitive dependencies (service → service → service → missing). Framework `ValidateOnBuild=true` was already rejected because AuthServer deliberately disables it—Program.cs loads Identity.Application MediatR handlers via assembly scan, but the host doesn't load the full dependency graph those handlers expect. Flipping ValidateOnBuild=true creates noisy false-positives on unused handler chains.

**Decision:** Instead of framework validation, enumerate every `public ControllerBase` in AuthServer assembly and explicitly resolve each via `ActivatorUtilities.CreateInstance(sp, type)`. Cheap, silent on false-positives, catches the actual gap class (controller → transitive → missing).

## Technical Details

**New file 1:** `tests/unit/AqtCoreFW.AuthServer.Tests/Startup/AuthServerHostFixture.cs` (~95 LOC)
- `WebApplicationFactory<Program>` subclass
- Ephemeral Testcontainers MsSql (not shared SqlServerFixture—would import Api csproj → conflict on `Program` type)
- ConfigurationManager-phase env-var hoist for connection string override
- ConfigureTestServices removes unneeded seeder IHostedService (to avoid side effects)

**New file 2:** `tests/unit/AqtCoreFW.AuthServer.Tests/Startup/AuthServerStartupValidationTests.cs` (~80 LOC)
- **Test #1 `Host_Builds`:** Smoke test. Validates the fixture builds + Testcontainer spins + migrations apply. Exercises explicit GetRequiredService chain at Program.cs:331-349 (Hangfire JobStorage, IPasswordVerifyTimingDecoy, IdentityDbContext, MessagingDbContext).
- **Test #2 `AllControllers_Resolve`:** The critical guard. Reflects all `public ControllerBase` types in AuthServer assembly, attempts `ActivatorUtilities.CreateInstance(sp, type)` on each. Catches missing transitive deps (service → service → service → **gap**). Clear error message names the controller + root cause.

**Negative validation (the insight):** 
Commented out `AddSharedKernelGuidGenerator()` at Program.cs:177 → ran tests:
- Test #1: **PASS** — Host builds fine because `IGuidGenerator` isn't in the explicit GetRequiredService chain (line 331-349)
- Test #2: **FAIL** — Clear message: "Unable to resolve IGuidGenerator while activating AuditService" + lists 3 controllers (AuthorizationController, ExternalLoginController, MfaChallengeController)

**This proves both tests are needed.** Test #1 is necessary but not sufficient. Future maintainers reading this: do NOT delete Test #1 thinking Test #2 covers everything. Test #1 validates the *explicit* chain; Test #2 validates the *implicit* (controller-driven) chain. Both gaps matter.

**New file 3:** csproj changes
- +Microsoft.AspNetCore.Mvc.Testing (WebApplicationFactory)
- +Testcontainers.MsSql (isolated from SharedFixtures)

## Validation Trail

- **AuthServer.Tests:** 27 → 29/29 PASS (~24s including ~10s Testcontainer cold-start)
- **Architecture.Tests:** 26/26 PASS (no regression)
- **Code review:** PASS, 0 P0/P1, 3 P3 cosmetic (M1: clarified OpenIddict seeders run via direct `await`, not IHostedService)

## Files Changed

**Added:**
- `tests/unit/AqtCoreFW.AuthServer.Tests/Startup/AuthServerHostFixture.cs`
- `tests/unit/AqtCoreFW.AuthServer.Tests/Startup/AuthServerStartupValidationTests.cs`
- `tests/unit/AqtCoreFW.AuthServer.Tests/AuthServer.Tests.csproj` (updated with new refs)

**Not modified:**
- Program.cs remains unchanged; validation is passive observer only

## Lessons Learned

1. **Framework ValidateOnBuild != comprehensive:** It's coarse-grained (all services) vs. fine-grained (only services reachable from entry points). AuthServer's case proves selective validation can coexist with handler assembly-scan. Cost of enumerate: ~10 LOC. Cost of false-positives: hours of noise.

2. **Test #1 + Test #2 pairing:** Explicit chain validation (Test #1) catches gaps in *intentional* registrations (Hangfire, password verify decoy). Implicit chain validation (Test #2) catches gaps in *transitive* chains (controller → audit → guid). One test masks holes in the other.

3. **Negative test value:** Commenting out `AddSharedKernelGuidGenerator()` proved the design. This pattern (intentional feature deletion + re-run) should become standard for DI guards. Subagents should do this routinely when shipping architectural tests.

4. **Self-contained fixtures over shared:** Testcontainers cold-start (~10s per test session) is cheap compared to Program-type conflicts. Worth the duplication in this case. Lesson: isolation > reuse when type names collide across projects.

## Follow-ups Spawned (P3, not blocking)

- **FU1 (~2h):** Replicate guard for Api + Gateway hosts. Api uses `ValidateOnBuild=true` so likely covered; controller-enumeration is cheaper than waiting for integration test failure.
- **FU2 (~30m):** Relocate test to `tests/integration/AqtCoreFW.Startup.SmokeTests/` alongside existing smoke tests. Conceptually smoke, not unit.
- **FU3 (~1h):** Extract generic `HostDiArchGuard<TProgram>` helper for cross-host reuse (Api + AuthServer + Gateway).

## What This Didn't Cover

- Middleware constructor deps (theoretically same gap class, unlikely in practice—middleware deps usually pulled from controller chains anyway)
- IHostedService deps (none registered in AuthServer beyond fixture cleanup)
- Action Filter / Convention deps (edge case, YAGNI'd)

If a future incident lands on one of these, expand Test #2. Current coverage should handle 95% of AuthServer scenarios.

---

**Status:** DONE
**Summary:** Added preventive DI validation to catch transitive dependency gaps at startup, not on first live request. Two-test pattern (explicit + implicit) validates both intentional registrations and controller-reachable chains.
**Concerns:** None. Code review approved. Both tests pass. Startup time +12s (cold Testcontainer), accepted for reliability.
