# P2.3b — Tenant-Agnostic User Seeder (Parallel-Change)

**Date**: 2026-06-01 PM
**Severity**: Medium
**Component**: Identity.Infrastructure/Seeding
**Status**: Resolved

## What Happened

Shipped P2.3b: `TenantUserSeeder`, a tenant-agnostic identity seeder that replaces the user + user-role seeding steps in the BTC-only `BtcIdentitySeeder`. The seeder executes a 3-pass algorithm (coverage lead + level-weighted fill + dev-reserve) and binds users to roles derived from their org-unit position. Runs after `TenantRoleSeeder` (P2.3a), respects `TenantSeedProfiles` (BTC + BNV), and is gated `Identity:TenantUserSeed` (default OFF).

**Commits shipped:**
- `eb055972` — feat(seeding): tenant-agnostic user seeder (users + user-roles, parallel-change)
- `4f557647` — docs(changelog): v2.137p132 tenant-agnostic user seeder (P2.3b)

Working tree clean; `origin/main..HEAD = 0` (both commits pushed origin + bitbucket).

## The Brutal Truth

This was a mechanical generalization of a **working legacy seeder**, not a redesign. The slice felt low-risk in theory — P2.1 had already defined all the user fields on `TenantSeedProfile` (code prefix, lead/dev email prefixes, domain, user count, max level), so the only delta was parameterization. But the moment I started reading the code to verify the claim, I hit a correctness trap that wasn't obvious in the design docs: **the per-profile RNG seeding**.

Legacy `BtcIdentitySeeder.SeedUsersAsync` seeds the Bogus `Faker` + `Random` **once** at the method level. If you loop that call across multiple profiles without re-seeding, the first profile consumes the RNG state, so the second profile's generated names depend on whether the first ran — breaking reproducibility and output-equivalence. This isn't just a "nice to have" idempotency detail; it's a correctness bug waiting to happen on a reset-all where BTC seeding order becomes undefined.

The fix was a one-liner (re-seed inside the profile-loop), but catching it required reading the actual seeder code **before writing a line of new code**. That's the scout-first pattern working: advisor flagged it on code review, I confirmed the fix, and it's guarded by a test assertion that actually runs.

## Technical Details

**Files:**
- NEW `src/modules/identity/AqtCoreFW.Identity.Infrastructure/Seeding/TenantUserSeeder.cs` (118 lines, split into 3 partials: orchestrator + `.Users.cs` + `.UserRoles.cs`)
- NEW `TenantUserSeederOptions.cs` (config section `Identity:TenantUserSeed`)
- MODIFIED `InfrastructureRegistration.cs` (gated registration after `TenantRoleSeeder`)
- MODIFIED `src/tests/architecture/.../Helpers/CrossSchemaSqlScanner.cs` (whitelist cross-schema org-read)
- NEW `src/tests/AqtCoreFW.Tests.Integration/Identity/TenantUserSeederTests.cs` (6 facts, real-SQL via TestContainers)

**Design mirrors P2.3a:**
- `IHostedService` + `IIntegrationTestSeeder` interface
- Loops `TenantSeedProfiles.All` (BTC + BNV)
- Tenant-existence guard with skip-with-warning (so 2-restart convergence settles cleanly)
- Static testable core driven by a synthetic org tree (no cross-schema FK, only Guid PrimaryOrgUnitId)
- Reads each tenant's org tree via Dapper `SELECT PublicId AS Id, Code, [Level] FROM organization.OrgUnits`

**Key implementation:**
- 3-pass user population: Pass 1 (coverage lead per OrgUnit) → Pass 2 (level-weighted fill) → Pass 3 (dev-reserve pinned to L2)
- Position-derived role binding: `profile.RolePrefix + positionCode` (e.g. `BTC_DIRECTOR` for a director position)
- Resume-safe: uses `profile.UserCodePrefix` to count existing users; drift gate `existingCount == profile.UserCount`
- RNG re-seeded inside the per-profile call to guarantee reproducibility across tenants

**Cross-schema read gated:**
The seeder reads `organization.OrgUnits` from the identity module. This triggered `Architecture.NoModule_Should_Reference_AnotherModuleSchema_InRawSql` test (107/108 → 108/108 after whitelist). Added a stable whitelist entry (no plan-artifact reference — the reason is "seeder bootstrap, tenant-agnostic org tree required before user bindings").

## What We Tried

1. **Scout-first on the legacy code** — before implementing, read `BtcIdentitySeeder` verbatim to understand what "generalize" meant. Found: Bogus faker + Random seeded once at method level, no per-tenant isolation.
2. **Verify P2.1 contract** — checked whether `TenantSeedProfile` already carried all user-generation fields. Yes: `UserCodePrefix`, `LeadEmailPrefix`, `DevEmailPrefix`, `FillEmailPrefix`, `EmailDomain`, `UserCount`, `MaxLevel`. No SharedKernel change needed.
3. **Design re-seeding guard** — added per-profile Faker + Random re-seeding inside the loop. Guarded by test assertion: wipe + reseed yields byte-identical FullNames across runs.
4. **Cross-schema read** — Dapper read of `organization.OrgUnits` from the seeder; architecture test caught it; added whitelist.

## Root Cause Analysis

The fix was NOT about a missing feature; it was about **faithfully replicating a legacy behavior in a new context**. The legacy seeder worked (for BTC only) because it was called once. The new seeder would be called per-profile in a loop, so the RNG isolation had to be explicit.

Why wasn't this surfaced earlier? Because the plan docs said "mirror P2.3a design" — P2.3a (`TenantRoleSeeder`) doesn't have RNG concerns (roles are static lists). No one wrote down "if legacy seeder uses RNG, you must re-seed per-tenant." The correctness gap lived in the **code**, not the spec.

## Lessons Learned

1. **Scout-first on legacy codebases pays off.** Before generalizing a working component, read the actual code — not the design docs or memory from 2 weeks ago. The trap was buried in a few lines of seeder initialization.
2. **Test assertions are cheap insurance.** The faker reproducibility test (wipe + reseed, compare byte-equal FullNames) cost 3 lines; it proved the fix is real and will catch regressions.
3. **Idempotency gates don't guarantee short-circuits.** The gate is `existingCount == UserCount`, but the 3-pass algorithm with integer-division truncation means the total drifts below UserCount on some runs. The drift path is the steady state, not a rare edge case. Documented in code; not a bug, but a legacy characteristic to preserve for output-equivalence.
4. **Cross-schema reads compound across slices.** P2.2 swap surfaced the whitelist-needed pattern; P2.3b reinforces it. Every new raw-SQL read across schema boundaries must have a documented reason + whitelist entry. The architecture gate is load-bearing.
5. **Changelog ≠ depmap (process gap).** The slice ran gated-off (dormant), so `dependency-map.md` was skipped per prior P2.x precedent. But the changelog should ALWAYS record a shipped version, regardless of gate status. Added the v2.137p132 entry as a separate docs commit rather than letting it drift.

## Next Steps

**P2.3c = slice P2 FINAL (dependencies clear):**

1. Generalize `BtcAssignmentSeeder` (Organization module) to `TenantAssignmentSeeder` — same pattern: loop profiles, tenant-existence guard, parameterized by profile.
2. DELETE legacy `BtcIdentitySeeder` + `BtcAssignmentSeeder` (all user/user-role/assignment work now generic).
3. Flip `Identity:TenantUserSeed` + `Organization:TenantAssignmentSeed` Enabled to true in reset-all.ps1 env-overrides.
4. Wire both seeder completion markers into the reset-all.ps1 poll + remove the legacy BTC seeder marker.

**Unresolved carry-forward (P2.3c must address):**

1. **Runtime output-equivalence in live reset-all.ps1** — the synthetic-tree tests prove the contract (4-level + 2-level/22-unit BNV shape), but NOT the live ~10k-user BTC run. P2.3c must execute the full reset in dev and diff the output against the pre-swap baseline. Synthetic tests are load-bearing for logic; live tests are load-bearing for scale.

2. **17-vs-15 role gap** — `TenantRoleSeeder` seeds 15 position-derived roles per tenant (the canonical hierarchy). Legacy `BtcIdentitySeeder.Roles.cs` seeds 17 (includes `INSPECTOR` + `AUDITOR`, which are never position-bound — admin roles). P2.3c must confirm those 2 roles have a home before deleting the legacy seeder. If they're used by live workflow definitions, they must move to the new seeder.

3. **GUID churn on reset convergence** — since the idempotency gate never short-circuits (existingCount drifts below UserCount), the seeder re-mints user GUIDs on 3rd+ reboot. P2.3c must confirm the assignment seeder keys on a stable handle (EmployeeCode), not the user GUID, or that 2-restart reset masks the churn as the legacy approach relied on.

**Pre-existing unowned reds:**
- `SchemaUniversalsTests` datetime2(3) failure (unrelated, pre-existing)
- Open backlog: `IDENTITY-VAULT-ENABLED-DEAD-CONFIG` (T4, filed in P2.3a)

