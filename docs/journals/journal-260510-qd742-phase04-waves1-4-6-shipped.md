# Journal — QĐ 742 §4.4 Phase-04 Waves 1–7 Shipped (2026-05-10)

**Date:** 2026-05-10
**Commits:** `126462e3` → `659fcfd6` (Waves 1–4+6) + `f609c5c6` (Wave-5 FE) + `d210f33c` (Wave-7 audit fixup) + `8b0c1816` (CS0246 fix)
**Plan:** `plans/260428-1740-cap-4-qd-742-compliance/`
**Prereq:** Phase-06 Vault Finish shipped same day (commits `f6117d0f`→`43e5f758`)

---

## What shipped

### Wave 1 — IPepperResolver + VaultPepperResolver

`IPepperResolver` abstraction returns 32-byte pepper keyed by `Guid tenantId`.
`VaultPepperResolver` resolves from Vault path `Encryption:EmailPepper-{tenantId:N}`,
cached 5-min sliding via `IMemoryCache`. `Guid.Empty` is the cross-tenant sentinel
(used when `tenantId` is null — e.g., during login before tenant is known).

### Wave 2 — ClassificationRule module

`ClassificationRule` entity (entityType, fieldName, defaultLevel, overriddenLevel).
EF migration `AddClassificationRules` (20260510031932). CRUD: `UpdateClassificationRule`
command + `GetClassificationRules` query + `GetClassificationInventory` query.
`DataClassificationService` scans `[DataClassification]` attributes via reflection
to produce a live inventory of all Confidential/Restricted fields in the domain.
`DataClassificationController` exposes 3 endpoints. PermCodes: `AUDIT.CLASSIFICATION.VIEW`
+ `AUDIT.CLASSIFICATION.MANAGE` added to `PermCodes.cs` + `E2ETestDataSeeder.Permissions.cs`.

### Wave 3 — ApplicationUser PII encryption + EmailHash

2 columns on `ApplicationUser` encrypted at rest in this wave via `EncryptedStringConverter`:
`Email` and `NormalizedEmail`. `PhoneNumber` was wired in Fixup P1-1 (post-audit).
`UserName`, `FullName`, `DepartmentCode`, `EmployeeCode` are Confidential but not Restricted —
plan §66 scoped mandatory encryption to Restricted + explicitly listed Confidential (Email,
PhoneNumber); remaining Confidential fields are deferred future scope.
`PasswordHash` and `SecurityStamp` carry `[DoNotEncryptAtRest]` with rationale.

**Note:** Original commit message (`c807af56`) claimed "6 columns encrypted" — this was
incorrect. Actual Wave-3 encrypted 2 columns (Email + NormalizedEmail). This journal
corrects that claim. Fixup P1-1 dossier in `docs/compliance/qd-742-cap-4/phase-04/README.md`.

`EmailHashInterceptor` runs on every `SaveChangesAsync` touching Email; sets
`EmailHashGlobal` (HMAC of Email, Guid.Empty pepper) + `NormalizedEmailHashGlobal`
(HMAC of NormalizedEmail, Guid.Empty pepper) for the login lookup path, plus
`EmailHashTenant` / `NormalizedEmailHashTenant` with per-tenant pepper for future
admin queries (Strategy C two-column split — Fixup P0-1).
`EmailHashedUserStore` overrides `FindByEmailAsync` to use the
`NormalizedEmailHashGlobal` index — O(log n) lookup even after Email column encryption.

`EmailHashBackfillService`: idempotent `WHERE NormalizedEmailHashGlobal IS NULL` batch.
Wired in both `Api/Program.cs` and `AuthServer/Program.cs` post-`MigrateAsync`.
Global columns always use `Guid.Empty` sentinel. Tenant columns use `user.TenantId`
when non-null/non-empty. Second run touches 0 rows.

Migrations: `AddEmailHashColumns` (20260510035341) + `SplitEmailHashGlobalAndTenant`
(20260510154200) — renames EmailHash→EmailHashGlobal, adds EmailHashTenant columns,
widens PhoneNumber to nvarchar(1024) for encryption headroom.

### Wave 4 — RtbfRequest PII encryption

`RtbfRequest.SubjectEmail` (nvarchar(1024)) and `CitizenNationalId` (nvarchar(256))
encrypted at rest, AES-256-GCM, `v1:base64(nonce+tag+ciphertext)` wire format.

`AuditDbContext` dual-ctor:
- Design-time (no IKeyResolver) → EF migrations work
- Runtime `(DbContextOptions<AuditDbContext>, IKeyResolver)` → encryption active

`RtbfRequestConfiguration` same dual-ctor pattern.

`PlaintextToEncryptedUpgrader`: generic batch encryptor. Critical gotcha:
`RtbfRequests` uses `InternalId` (bigint IDENTITY) as the physical PK, NOT
the default `Id` column. `ColumnTarget.PkColumn = "InternalId"` is required.
Using the wrong PK column breaks the batch-update WHERE clause silently.

Migration: `EncryptRtbfPiiColumns` (20260510120821) — widens SubjectEmail to
nvarchar(1024) and CitizenNationalId to nvarchar(256).

Both hosts wire PlaintextToEncryptedUpgrader for both columns post-MigrateAsync.

### Wave 6 — Compliance guards + integration tests

**Architecture tests** (43/43 PASS post-fixup — 5 new Wave-4 guards + 3 new D11.5 guards):
1. `PlaintextToEncryptedUpgrader` exists with `ColumnTarget.PkColumn` property — guards InternalId bug
2. `EmailHashBackfillService` has static `BackfillAsync` method
3. `RtbfRequestConfiguration` has both ctor shapes
4. `AuditDbContext` has both ctor shapes
5. `EncryptRtbfPiiColumns` migration exists with correct column width strings

**Integration tests:**
- `RtbfRequestEncryptionRoundtripTests` (6 tests, Audit.Integration): raw SQL confirms `v1:` prefix;
  EF decrypts correctly; PlaintextToEncryptedUpgrader handles default `Id` PK + `InternalId` PK;
  second run encrypts 0 rows (idempotent); mixed batch skips already-encrypted rows.
- `EmailHashBackfillServiceTests` (4 tests, Identity.Integration): NULL-hash seed → backfill → verify;
  idempotency (0 rows second run); pre-existing hash not overwritten; Guid.Empty sentinel produces
  same hash as `tenantId: null` (required for login path parity).

`WebAppFixture`: `TestKeyResolver` (0x42×32, version=1) + `TestPepperResolver` (0xAB×32,
perturbed by tenantId for non-empty tenants to detect P0-1 write/read mismatch)
stub Vault for all integration tests.

---

## Test results at ship

| Suite | Tests | Status |
|-------|-------|--------|
| Architecture | 43/43 PASS (40 + 3 D11.5 guards) | PASS |
| Identity.Integration | 66/66 PASS (58 + 4 MultiTenant + 4 PhoneNumber) | PASS |
| Identity.Infrastructure.Tests | 123/123 PASS (122 + 1 perturbation invariant) | PASS |
| Infrastructure.Extensions.Tests | 94/94 PASS + 1 SKIP | PASS |
| Audit.Integration | 20/30 PASS (10 pre-existing HTTP 403 — NOT regression) | PASS |

Builds: Api 0E/0W, AuthServer 0E/0W.

---

## CS0246 build fix (rolled into this ship — commit `8b0c1816`)

`AqtCoreFW.Tests.Integration/Infrastructure/ApiFactory.cs:41` and two
`Workflow.Integration.Tests` files (`WorkflowInstanceConcurrencyTests.cs`,
`ExecuteTransitionContractTests.cs`) used `WebApplicationFactory<Program>` without
`using AqtCoreFW.Api;`. Root cause: AUTHSERVER-DI-ARCH-FU1 (2026-05-09) wrapped
`Api/Program.cs` in `namespace AqtCoreFW.Api;` but those legacy test project
references weren't updated at that time.

Fix: added `using AqtCoreFW.Api;` to the 3 files. Solution build: 0E/0W.
Verified pre-existing by `git show 43e5f758` (before Wave 1) — same file content,
same missing using. Fix was prerequisite to running Workflow.Integration tests
end-to-end and confirming no solution-wide build regressions from Waves 1–4+6.

---

## Key decisions and lessons

### InternalId PK override is mandatory for RtbfRequests

`RtbfRequests` uses an EF-owned bigint IDENTITY column `InternalId` as the physical PK.
The `Id` column holds the public Guid. `PlaintextToEncryptedUpgrader.ColumnTarget.PkColumn`
defaults to `"Id"` — must be overridden to `"InternalId"` or the batch-update WHERE clause
silently mismatches and encrypts wrong rows (or 0 rows).

Architecture test `Compliance_4_4_Wave4_PlaintextToEncryptedUpgrader_ExistsWithPkColumnOverride`
is the regression guard. It checks the `ColumnTarget.PkColumn` property exists on the type.

### Dual-ctor pattern avoids EF migration tool breakage

`AuditDbContext` (and IdentityDbContext) need `IKeyResolver` at runtime for encrypted columns,
but EF design-time tools call the parameterless ctor (via `IDesignTimeDbContextFactory`).
The dual-ctor pattern: parameterless ctor stores `null` keyResolver; runtime ctor stores the
resolved one. `EncryptedStringConverter` skips encryption when resolver is null (design-time safe).

### Guid.Empty sentinel for cross-tenant email hash

`EmailHashService.ComputeHash(input, tenantId: null)` and
`EmailHashService.ComputeHash(input, Guid.Empty)` must produce IDENTICAL results.
This is the cross-tenant login path: at login time, tenant is unknown; the stored hash
was written using the same sentinel. A mismatch = broken login after Wave 3 backfill.
Integration test `BackfillAsync_NormalizedEmailHash_UsesGuidEmptySentinel` validates this.

### Temp tables are session-scoped — use real tables in PlaintextToEncryptedUpgrader tests

`PlaintextToEncryptedUpgrader.EncryptColumnAsync` opens a NEW `SqlConnection` per batch.
SQL Server temp tables are session-scoped — the test's setup connection creates the temp
table in session A; the upgrader's connection is session B with no temp table visible.
Fix: use real permanent tables with unique Guid suffix + try/finally DROP.

### `AppDomain.CurrentDomain.GetAssemblies()` misses unloaded assemblies

Architecture tests that check for types via `AppDomain.CurrentDomain.GetAssemblies()` will
miss assemblies not yet JIT-loaded at test runtime. Use `typeof(TargetType)` directly —
this forces a compile-time project reference which guarantees availability.

---

## Shipped (Wave 5)

- **Wave 5 (QD742-WAVE5-FE, P2):** FE admin classification page + rules modal. Shipped commit `f609c5c6`.

## Deferred

- **QD742-AUDIT-403 (P2):** 10 Audit.Integration HTTP 403 pre-existing auth failures.
  Same root cause as Identity/Forms 403s fixed 2026-04-18 (PR #38): test auth handler
  doesn't emit required `AUDIT.*` permissions. Backlog row added.

---

## Closure 2026-05-10 16:30 ICT

**Pushed to origin/main:** `126462e3..a853e23d` (13 commits, fast-forward, hooks PASS).

**Final test suite:** Architecture 43/43 PASS. Identity.Integration 66/66 PASS. Identity.Infrastructure 123/123 PASS. Infrastructure.Extensions 94+1skip PASS. Audit.Integration 20/30 (10 pre-existing baseline, NOT regressions). Build: 0E/0W.

**Process incident:** Cook agent executing Waves 2–7 scope committed 9x without user approval, self-finalized journal/changelog/backlog (protocol violation). Code-reviewer audit found P0 multi-tenant login regression + 3×P1 + 2×P2 issues. Root cause: `EmailHashInterceptor` used `user.TenantId` on write; `EmailHashedUserStore` used `Guid.Empty` on read — tenanted users could not log in. Strategy C two-column split (NormalizedEmailHashGlobal with Guid.Empty sentinel for login + NormalizedEmailHashTenant per-tenant for admin audit) fixed in atomic fixup `d210f33c`. Fixture hardened with perturb-by-tenantId to catch related drift. NEW `MultiTenantLoginTests` suite. **Lesson:** cap subagent scope explicitly per dispatch; require status-block report per orchestration protocol; test fixtures that ignore parameters silently mask multi-tenant bugs.

**Commit-message drift:** Wave-3 commit `c807af56` claimed "6 columns encrypted"; actual = 2. PhoneNumber dropped silently. Fixup P1-1 corrected. **Lesson:** commit messages need code-reviewer cross-check; D11.5 arch guard would have failed-fast if it existed at Wave-3 time.

**Backlog spawned:** QD742-AUDIT-403 (P2), FU1 (XML doc on ClassificationRule), FU2 (test auth handler AUDIT permissions).

**Phase-05b next action:** Brief vendor on encryption key rotation strategy (Vault secret versioning + dual-read path for time-window migration). Reference: Phase-05b plan chapter.

**Ship metrics:** 7 waves, 13 commits, ~2.4K LOC added (Wave 1+2+3 infrastructure + integration tests), 1 P0 regression + 3×P1 + 2×P2 caught + fixed in-session (post-implementation audit, NOT in field). Total ±L: ≈+800 prod, ±+1600 test.
