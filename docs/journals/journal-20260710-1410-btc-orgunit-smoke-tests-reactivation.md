# BTC Org-Unit Smoke Tests Reactivation — MFA Fixture Gap + Speculative Assertion Trap

**Date**: 2026-07-10 14:10  
**Severity**: Low  
**Component**: Organization.Tests (smoke)  
**Status**: Resolved  

## What Happened

Reactivated 3 skipped smoke tests in `OrgUnitsControllerSmokeTests.cs` (`GET_OrgUnitsTree_ReturnsFullHierarchy_ForBtcTenant` + 2 sibling reads). Plan `260528-1905` targeted 15 skipped tests across 3 files, but INVARIANT 260522-A audit found only 3 remained live — the other 9+3 (Seeding, Architecture) were already un-skipped by `394e2efb` (Hangfire env-guard) + `OrgTreeSeeder` direct wiring.

Real blocker: `OrgUnitsController` carries class-level `[RequiresMfa]`. The fixture's 3-arg `CreateAuthenticatedClient(userId, tenantId)` overload defaulted `includeMfaClaim:false` → all GETs returned 403. Fixed: switched to 4-arg `CreateAuthenticatedClient(userId, tenantId, includeMfaClaim: true, roles: "Admin")`. The `roles` param is immaterial for GET (no RBAC policy), but MFA is the gate.

Secondary finding on reactivation: the first test assertion was speculatively written (test was skipped since genesis, never ran) — it expected `data` as `JsonValueKind.Object`, but the contract `GetOrgTreeQuery : IQuery<IReadOnlyList<OrgUnitDto>>` returns a flat array (clients build tree from `ParentId`). Corrected to `JsonValueKind.Array` + non-empty length check + first-element schema sanity (`code` / `level` camelCase).

## The Brutal Truth

This is the brutal lesson of reactivating skipped tests: it's NOT a rename. A test skipped since creation can carry a latent wrong assertion that only fires on reactivation. We wrote the assertion speculatively, never tested it, and buried it under `[Skip]` for years. The plan's own red-team warned about vacuous asserts (F9), but this one was worse — it was *wrong*, not just tautological. The MFA fixture gap is equally embarrassing: `[RequiresMfa]` on a controller is a real gate; the fixture should have modeled it from day one. Instead, we shipped smoke tests that never ran because a fixture default was inverted.

## Technical Details

- **File touched**: `tests/unit/AqtCoreFW.Organization.Tests/Smoke/OrgUnitsControllerSmokeTests.cs` (test-only, 0 production code)
- **Assertion fix**: `JsonValueKind.Object` → `JsonValueKind.Array` + `GetArrayLength() > 0` + schema validation
- **Fixture call**: 3-arg `CreateAuthenticatedClient(userId, tenantId)` → 4-arg `CreateAuthenticatedClient(userId, tenantId, includeMfaClaim: true, roles: "Admin")`
- **Verification**: Full `AqtCoreFW.Organization.Tests` local-SQL warm-reuse: 79/79 pass, 0 skip, 0 fail (was 76 + 3 skip). Isolated re-run (oracle): 3 Smoke tests green, build 0W/0E under `-warnaserror`.
- **Code-review**: APPROVE — all 5 checks empirically verified (contract match, 4-arg binding, camelCase serialization, no production contract touched, no lint/type issues).
- **Pre-existing Low notes** (tightened same-slice): two near-vacuous `by-code` equality tests (low discriminant); RLS-comment overstated ("filters to tenant-visible only" — actually a smoke fixture isolation detail, not an RLS enforcement). Both documented, harmless.

## What We Tried

Straightforward: (1) audit the plan via INVARIANT-A (grep `Skip=` across 3 files), (2) discover 2 of 3 root-cause layers already resolved, (3) fix L3 (the live fixture gap), (4) surface and correct the assertion trap on reactivation.

No exploratory failures — the audit was disciplined because the backlog row had promised 15 fixes but the empirical probe found only 3 needed.

## Root Cause Analysis

**The MFA fixture gap**: `OrgUnitsController` class-level `[RequiresMfa]` is enforced by an `IAuthenticationFilter` (not an authorization policy). The fixture's 3-arg helper was written for role-based tests, where the default `includeMfaClaim:false` is safe (RBAC policy doesn't reference MFA). Smoke tests needed MFA but the fixture defaulted against it — an assumption that went unquestioned because the tests stayed skipped.

**The speculative assertion**: When you write a test assertion *without executing it*, you're guessing at the contract. The guess sounded reasonable ("org tree endpoint returns an object"), but the actual contract is a flat array (clients do the tree-building). The test never caught this because it never ran.

**Why both lived**: Skipped tests are *invisible to CI*. A skipped test is a debt certificate with no maturity date. The plan assumed both issues were urgent enough to block 15+ tests; the repo proved only 1 was live.

## Lessons Learned

1. **Reactivating a skipped test is a code-review gate, not a rename.** Re-verify the assertion against the REAL contract (read the endpoint docstring, the DTO definition, the seeder output). Vacuous assertions are caught; *wrong* assertions pass a skipped-test re-run.

2. **Fixture defaults must model the guard they're testing against.** A class-level `[RequiresMfa]` is as real as an RBAC policy. Don't default `includeMfaClaim:false` and assume smoke tests don't need MFA. The fixture should have offered both overloads and labeled them clearly (e.g., `CreateAuthenticatedClientWithoutMfa` vs `CreateAuthenticatedClientWithMfa`).

3. **Backlog row empirical audit catches decay faster than repo discipline.** The plan said "15 skips, 5–7h effort"; the audit said "3 skips, 2–3h effort." Two pre-emptive fixes (`394e2efb` + `OrgTreeSeeder` wiring) had already resolved half the scope. INVARIANT-A is cheaper than scope creep.

## Next Steps

- Plan `260528-1905` phases 02+03 CLOSED — all 3 reactivated tests green, assertion corrected, fixture pattern fixed for future smoke tests.
- CI-rename (surface 9 `…IntegrationTests` in the CI unit surface) remains deferred — CI is PARKED this phase, owner @PM.
- No follow-up needed; 0 production code touched, 0 blast-radius.

---

**Status**: DONE  
**Filename written**: `docs/journals/journal-20260710-1410-btc-orgunit-smoke-tests-reactivation.md`
