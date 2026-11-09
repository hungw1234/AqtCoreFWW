# Journal — P2.3a role seeder wrap + tenant-scoped role assignment

**Date:** 2026-06-01 | **Version:** v2.137p131 | **Session arc:** `/aqt-next` → verify P2.3a → `/ck-debug` → `/fix` (Vault stubs) → code-review → full refactor (auth)

## What shipped

1. **P2.3a (parallel-change, gated OFF):** `TenantRoleSeeder` seeds 17 gov-hierarchy business roles per tenant (BTC_/BNV_), legacy `BtcIdentitySeeder` left live. Migration `ScopeRoleNameToTenant` swaps AspNetRoles name-uniqueness GLOBAL → `(TenantId, NormalizedName)`.
2. **Auth fix (full refactor):** `AssignRole`/`SyncUserRoles` rewritten name-based-global → RoleId tenant-scoped via new `IRoleRepository.GetByNameInTenantScopeAsync` + `IUserRepository.{GetRoleIds,AddToRole,RemoveFromRoles}`.
3. **Test infra (`ad1f5e0f`):** `FakePepperResolver` + `FakeKeyResolver` stubs in `ApiFactory` (Vault-backed resolvers were unstubbed → user-create 500).
4. **Docs:** depmap p131 (fixed a stamp collision — see below), changelog p131, backlog row `IDENTITY-VAULT-ENABLED-DEAD-CONFIG`.

## Decisions & lessons

- **Stamp collision caught before `replace_all`:** the P2.3a depmap note was stamped `v2.137p121`, but p121 is a *real* prior version (WF-POOL-TASK-CANDIDATE-INDEX-WRITE-GAP). A blind replace_all would have corrupted the genuine entry. Lesson: grep occurrences before bulk-editing a version stamp; treat doc version numbers as potentially-colliding.
- **Verify before trusting "done":** the prior session pre-wrote "Code-review APPROVE" + "3 integration tests" in depmap; reality was review-not-run + 1 `[Fact]` + an uncovered tenant-absent path. Corrected the doc to match code, ran EF `has-pending` (clean) + tests (real) — `MigrateAsync` success alone does NOT prove snapshot consistency.
- **Symptom-log ≠ root cause (2× reframe in `/ck-debug`):** the user-create 500 logged a pepper Vault error first — but the `EmailHashInterceptor` *catches* it. The actual throw was one layer up in `EmailHashedUserStore.FindByEmailAsync` (read path, no catch). Then after stubbing the pepper, a *second* Vault dependency surfaced (encryption key via EncryptedStringConverter). Only reading the "Unhandled exception" stack — not the first Error line — found each real cause. Fix incomplete until output re-read after each step.
- **Discriminating stash-test settled pre-existing vs regression:** the 2 user-create reds were proven pre-existing by stashing P2.3a, rebuilding HEAD, re-running — identical 500. Count-match with memory was a coincidence to verify, not trust.
- **Audit finding validated against real threat model:** code-review rated the name-ambiguity High. Scout (`user-list-page.tsx:40` hardcodes 4 system roles; business roles assigned via group roleId-based path) showed the dangerous scenario is unreachable by any product path — and the base store throws (loud 500), not silent mis-assign. Severity was really Low. **Surfaced the corrected picture to the user (per rule §3) rather than silently downgrading or accepting** — user chose the full refactor with eyes open (likely near-term business-role-user-assign UX).
- **Full refactor was real auth surgery:** name-based `UserManager`/`RoleManager` APIs (`AddToRoles`/`GetRoles`/`RemoveFromRoles`) are all global; the fix needed add/remove-by-RoleId → a new `IUserRepository` seam (UserRepository gained `IdentityDbContext`). Architecture tests 108/108 confirm layering held.

## Verification

build 0E · 2 new tenant-scope facts (non-vacuous) · Identity ns 29/29 · Identity unit 245+154 · Architecture 108/108 · full integration assembly **187/188** (1 pre-existing datetime2 red).

## Carry-forward

- **P2.3b** = generalize users/userroles/assignment for both tenants + delete legacy `BtcIdentitySeeder`/`BtcAssignmentSeeder`. The tenant-scoped assignment fix is a **prerequisite** now satisfied (BNV users can be assigned roles deterministically).
- `IDENTITY-VAULT-ENABLED-DEAD-CONFIG` (T4) — `Vault:Enabled` flag read by nothing.
- Pre-existing reds unowned: `SchemaUniversalsTests` datetime2(3); `UsersApiTests` were fixed by `ad1f5e0f`.
- Push pending: `ad1f5e0f` + the 4 p131 commits.
