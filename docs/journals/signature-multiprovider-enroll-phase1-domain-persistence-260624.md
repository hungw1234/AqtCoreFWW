# Signature Multi-Provider Enroll — Phase 1 Domain & Persistence Shipped (Code Review Found Blind Spot)

**Date:** 2026-06-24 · **Severity:** High (tenant isolation) · **Component:** Identity.Domain, Identity.Infrastructure, Identity.Application · **Status:** Resolved (post-review fix applied)

## What Happened

Phase 1 (Domain + Persistence) of plan 260623-1950 shipped: 26-file commit `3c4f3ac8`, touched Domain entities (SignerCertEnrollment + UserSignatureProfile), migrations (2), repositories, command handlers. Implementation team reported all tests green. Code reviewer returned CHANGES_REQUESTED and **caught a critical tenant-isolation hole that the architecture test suite was BLIND to**.

## The Brutal Truth

We shipped a PII table (`UserSignatureProfiles`) into a modular DbContext (IdentityDbContext) **with zero RLS policy** — the table was added to a migration without the `RlsMigrationHelper.EnsurePolicy` call. The architecture guard (`SharedInfraTenantTableIsolationGuardTests`) swept only the *shared-infrastructure* DbContext and was completely unaware module DbContexts exist. Architecture tests reported 145/145 passing. That number was a lie — it just meant the scanner never looked at the right place.

The reviewer (human eyes on code) caught what 145 architecture tests missed. That stings.

## Technical Details

**The hole:**
- Migration `20260624043535_AddUserSignatureProfile` created table `UserSignatureProfiles(UserId, TenantId, IsDefault, ImageUrl, ...)` with RLS-suitable structure (TenantId column present).
- RLS policy NOT added (no `RlsMigrationHelper.EnsurePolicy("UserSignatureProfiles", "TenantId")` call).
- Schema assumed least-privilege database user (non-owner) — under that assumption, query without RLS = **zero rows returned silently, feature breaks in production**.
- Worst case (if db_owner still active in prod): cross-tenant data leakage.

**Why the guard missed it:**
- `SharedInfraTenantTableIsolationGuardTests` uses reflection over `AqtCoreFW.Infrastructure.Persistence` assembly → scans DbContext types in that assembly only.
- `IdentityDbContext` is in `AqtCoreFW.Identity.Infrastructure.Persistence` (different assembly) — never traversed.
- The guard had a false sense of coverage because it worked for shared-infra tables; no guard existed for module-specific DbContexts.

**High-2 & High-1 (secondary issues caught together):**
- H5 guard (SigningNationalId lock when enrollment exists): had a stub test returning `false` always → stub could be silently deleted/inverted. Added 3 real tests (positive + skip-case).
- Migration backfill tests (H1 teeth): seeded via factory, never executed actual SQL UPDATE statement → teeth-tests were phantom. Renamed honestly + documented SQL-coverage gap.

## What We Tried

1. **Local verification (implementer):** Ran all 3 test projects (Domain/Application/Integration), 782 tests green. Signed off.
2. **Code review:** Reviewer applied red-team discipline — read the migration file verbatim, traced RLS call, found it missing. Returned changes-requested.
3. **Fix:** Added `RlsMigrationHelper` call in migration + added module-DbContext RLS coverage test (follows-up; deferred to next slice).

## Root Cause Analysis

**Layer 1: Test coverage design flaw.** The architecture guard only reflected module DbContexts in the *shared* assembly. It was a proxy war — solving "all shared-infra tables must have RLS" while silently assuming "module DbContexts don't exist" or "module DbContexts have no tenant-scoped tables." Neither assumption was written down, so both decayed.

**Layer 2: False confidence from color (145/145).** When a test suite reports a number (145 passing guards), developers treat that as "tenant isolation verified." It wasn't. It was "shared-infra tenant isolation verified," full stop. The number should have been qualified, but it wasn't.

**Layer 3: Implementer didn't re-read the migration for RLS.** The checklist (TDD, peer-review, test-run) worked. But no step said "read the actual SQL — does it call RlsMigrationHelper?" That's a gap in the Done definition.

## Lessons Learned

1. **Verify-the-verifier.** Architecture tests passing is not the gate. Independent code-review caught what 145 tests missed. "All tests green" from the implementer is a starting signal, not a finish line. Require human eyes on risky code (tenant isolation, migrations, public contracts).

2. **Guard scope decay.** When a guard says "checked X," write exactly what X is (e.g., "SharedInfraTenantTableIsolationGuardTests: enforces RLS on all DbSets in `AqtCoreFW.Infrastructure.Persistence` DbContext only; module DbContexts NOT in scope"). Document the boundary explicitly; otherwise the next person assumes it's universal.

3. **Color is a liar.** 145/145 feels like "everything," but it's only "everything we looked at." A test suite should either be comprehensive (all DbContexts) or scoped (shared-infra only, clearly named). Partition confidence: don't mix "verified" and "in scope" without saying which.

4. **Teeth tests need SQL.** Factory-seeded tests pass; SQL-migration tests fail silently. For migration logic (backfill, policy application), the test must run the actual UPDATE/trigger that production will run, not just bootstrap via ORM factory. Name tests honestly: `MigrationBackfillTeeth_*` implies SQL execution; if it's just factory-seeded, call it `MigrationLogicSmoke_*`.

5. **Modular DbContexts need module-specific RLS tests.** SharedInfraTenantTableIsolationGuardTests is not enough. Follow-up: add a parallel guard (`ModuleDbContextTenantTableIsolationGuardTests`) that reflects all module DbContexts and enforces RLS on any DbSet<T> where T has TenantId property. (Deferred to P2 slice.)

## Next Steps

1. **Immediate (done):** Added `RlsMigrationHelper.EnsurePolicy` call in migration. Code-review approved post-fix. All tests re-run: 145/145 arch, 133/133 Domain, 331/331 Application, 318/318 Integration — GREEN.

2. **Follow-up (backlog, before P2 ships):** Implement module-DbContext RLS coverage guard to prevent recurrence. Scope: sweep all module DbContexts, assert any DbSet with TenantId property has RLS policy in corresponding migration. Red-team review before merge.

3. **Process:** Add migration-RLS checklist to definition-of-done for any task touching IdentityDbContext / domain tables. Checklist step: "Read the migration file, assert RlsMigrationHelper is called for every tenant-scoped table."

## Verify (final state, post-fix)

- Build: 0E, 0W
- Identity.Domain.Tests: 133/133 ✓
- Identity.Application.Tests: 331/331 ✓
- Identity.Application.Tests (signing-specific): 52 ✓
- Identity.Integration.Tests: 318/318 ✓
- Architecture guard suite: 145/145 ✓
- Code review: APPROVED (post-fix)

## P2 readiness

P2 (multi-provider enroll logic: sync-OTP RemoteSignHash + USB raw-PKCS#1 reuse VerifyRawAsync, switch on Kind, net-new bind-user nonce) is unblocked. 3 blocker spikes resolved (Mysign sync-OTP ✓, PDF-XObject pivot ✓, raw-PKCS#1 ✓). RLS follow-up guard owned, non-blocking.

---

**Emotional reality:** Shipping code that passes all internal tests only to have review catch a blind-spot is humbling. The guard was working — just not on the right things. The architecture suite gained a false sense of completeness. This is the cost of compartmentalizing tests; the cost shows up at code-review time, not CI time. That's actually the right place to catch it, but it means "CI green ≠ shipped." We learned it the expensive way.
