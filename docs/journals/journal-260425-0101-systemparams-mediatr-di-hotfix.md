# SystemParams MediatR DI Registration Hotfix

**Date:** 2026-04-25 01:01
**Severity:** High
**Component:** SystemParams module, DI container, v2.131p4 regression
**Status:** Resolved + preventive arch test added

## What Happened

v2.131p4 shipped with 9 broken endpoints: GET /api/v1/admin/announcements, POST /system-params, GET /feature-flags, and others across SystemParams module. All failed with:
```
No service for type 'MediatR.IRequestHandler`2[GetAllAnnouncementsQuery, ...]' has been registered.
```

Curl test to API revealed 500 before handlers could run. Regression silent in test suite.

## The Brutal Truth

We shipped a refactored v2.131p4 boasting "NetArchTest 3/3 PASS" — which doesn't validate runtime DI. The module added ~20 MediatR handlers across 4 modules but the SystemParams registration method was the only one that didn't call `AddMediatR`. That's a careless miss. We had 19 other modules doing it right and still forgot one. The integration tests? Only tested anonymous 401 paths, never the happy path. Architecture tests verified control-flow rules, not "is the handler actually registered in the container." We trusted the architecture gate to catch everything and it didn't.

## Technical Details

**Root cause:** Git forensics (commit `7e16e164`, 2026-04-24 17:10) shows SystemParamsRegistration.cs added handlers + validators but **zero AddMediatR call**. Diff:
```csharp
using AqtCoreFW.SystemParams.Application.Commands;
// ... handlers exist in Application.Queries/Commands
// MISSING: public static void RegisterServices(IServiceCollection services, IConfiguration _)
//          { services.AddMediatR(...); }
```

**Why tests missed it:**
1. `AnnouncementEndpointsTests` — only 401 anonymous (returns before MediatR pipeline) + /active (not MediatR)
2. `ThinControllerTests` — architecture rule (no DB access), not DI symmetry
3. `ModuleIsolationTests.AllModules_CommandHandlers_Should_HaveHandlerSuffix` — scanned only Application layer, not actual registration invocation

**Endpoints down:** 9 total
- Announcements CRUD (GET/POST/PUT/DELETE) + public /active
- SystemParams GET/Update
- FeatureFlags list

## What We Tried

1. Grep handlers exist → yes, all present
2. Grep AddMediatR across codebase → 19 modules have it, SystemParams missing
3. Read SystemParamsRegistration.cs → confirmed missing AddMediatR call
4. Fixed with 3-line `RegisterServicesFromAssembly(typeof(SystemParamsRegistration).Assembly)` call
5. Wrote preventive arch test: `ModuleMediatRRegistrationTests.cs` — invokes each module's registration method with stub IConfiguration, counts concrete IRequestHandler types. All 16 modules pass (~13s runtime).

## Root Cause Analysis

**Immediate:** Blind spot in v2.131p4 refactor. Handlers added, registration skipped for one module.

**Why it slipped:**
- RefactorPR description said "verify with NetArchTest" — architecture test ≠ DI test. We conflated "correct structure" with "correct wiring."
- Integration tests only tested error paths (401), not happy paths that invoke handlers.
- No pre-refactor DI symmetry verification test across the monorepo.

## Lessons Learned

1. **Architecture tests don't validate DI.** NetArchTest 3/3 PASS means control flow + layering are right. It doesn't mean handlers are registered. Need separate DI symmetry test.
2. **Integration test coverage was incomplete.** Testing anonymous paths only meant the handler pipeline was never exercised. Add positive-path authenticated tests.
3. **Reflection-based DI tests are cheap.** Writing ModuleMediatRRegistrationTests took ~30min and would have caught this exact pattern at test time. Worth doing before shipping refactors that touch DI.

## Next Steps

**Immediate (DONE):**
- Fixed: commit `fcb64e1b` (AddMediatR to SystemParamsRegistration)
- Added preventive test: commit `c18bb246` (ModuleMediatRRegistrationTests, all 16 modules pass)

**Follow-up (out of scope, noted in memory):**
- Cases.Infrastructure has architecture test violation: InfrastructureRegistration + CaseStatusEnumContributor reference another module directly. Marked for future audit.
- Before v2.133 ships: add ModuleMediatRRegistrationTests to CI gate. This test catches "handler exists but registration missing" across all refactors.

## Decision Record

Chose reflection-invoke of actual registration methods (not pure symmetry verification) because it catches "exists but not registered" — the exact pattern we hit. Symmetry checks would pass even if registration was missing. Wrapped in try/catch (Redis/SignalR deps not stubbable, but MediatR registration is early + side-effect-free, so partial failures still capture handler descriptors). Scanned Infrastructure-only in SystemParams fix — Grep confirmed zero IRequestHandler in Application assembly.

---

**Artifacts:** 
- Report: `plans/reports/debugger-260425-0101-systemparams-mediatr-missing-registration.md`
- Fix commit: `fcb64e1b`
- Arch test commit: `c18bb246`
