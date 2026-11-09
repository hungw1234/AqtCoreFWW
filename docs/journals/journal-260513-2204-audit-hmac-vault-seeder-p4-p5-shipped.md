# Journal — AUDIT-HMAC P4 + P5 shipped (2026-05-13 22:04 ICT)

## Context

Continuation of `plans/260513-1805-audit-hmac-vault-seeder-sweep/` after the
21:25 P1+P2+P3a + P6-finalize commit `92fe839b`. Goal of this sub-session:
ship Phase-04 (seeder defense sweep) and Phase-05 (tests + arch guards) in
`/ck:cook --auto` mode.

## Deliverables

### Commit 1 — `2d91bb9e` `feat(seeders): per-unit defense wrap on startup loop seeders`

Per-unit `try { ... } catch (OperationCanceledException) { throw; } catch
(Exception ex) { skipped++; LogError; continue; }` applied to 4 loop-bearing
startup paths so a single Vault-pepper miss or bad row no longer aborts the
whole batch:

- `EmailHashBackfillService.BackfillAsync` — per-user wrap around
  `PopulateHashes`. On throw, `dbContext.Entry(user).State = EntityState.Unchanged`
  reverts tracked mutations so the same `SaveChangesAsync` still persists the
  survivors; the failed user stays hash-NULL and is retried on the next
  startup. Log redacts to `user.Id` (no decrypted Email PII).
- `TenantBackfillSeeder.BackfillTenantsAsync` — signature break from
  `Task<int>` to `Task<(int Inserted, int Skipped)>` (+ `ILogger logger`
  param). Per-tenant `ExecuteAsync` INSERT wrapped. Sole non-test caller
  (`StartAsync`) and both integration tests updated in the same commit.
- `BtcIdentitySeeder.SeedUsersAsync` — two local helpers: `TryAddUser`
  (synchronous, wraps `BuildUser+batch.Add`) and `TryFlushAsync` (async,
  wraps `FlushAsync` with `ChangeTracker.Clear()` + `batch.Clear()` on throw).
  Captured `skipped` counter is single-threaded async — safe. Skip counter
  increments by `batch.Count` on flush failure (honest worst-case bookkeeping).
- `E2ETestDataSeeder.StartAsync` — 9 sequential `Seed*Async` calls each
  wrapped via local `TryStepAsync(name, action)`. Dev-only env guard still
  first.

Plan named these exact 4 + the in-session `OpenIddictDataSeeder` wrap (already
hot-fixed in P3a). Explore-pass inventory found 10+ additional loop-bearing
seeders (BtcOrgUnit L2–L4, BtcAssignment, WorkflowTaxonomy, NotificationTemplate,
SystemParam) — filed as `P4-FU` per YAGNI; none are Vault-pepper consumers.

### Commit 2 — `1d1c6a52` `test(audit): VaultAuditHmacKeyResolver unit tests + secret-config arch guards`

- `tests/unit/AqtCoreFW.Audit.Infrastructure.Tests/Services/VaultAuditHmacKeyResolverTests.cs`
  — 6 cases mirroring `VaultPepperResolverTests`: `VaultHit`, `CacheHit`
  (`Received(1)` on `IVaultClient`), `LengthMismatch_DoesNotFallbackToLegacy`
  (the critical no-silent-downgrade invariant — a 31B Vault value must escape
  as `InvalidOperationException`, never quietly demote to legacy config),
  `NoLegacy_Throws`, `Base64Legacy`, `Utf8Legacy`.
- `tests/architecture/AqtCoreFW.Architecture.Tests/SecretConfigBypassGuardTests.cs`
  — 3 cases. (a) `IConfiguration_SecretKey_Reads_AreAllowlisted`: substring
  regex scan of `src/**/*.cs` for
  `(configuration|cfg|_config|builder.Configuration)["...Hmac/Pepper/EncryptionKey/FieldKey/SecretKey..."]`,
  allowlist `ProductionSecretValidator.cs` + `VaultAuditHmacKeyResolver.cs`,
  fails on any other source file. (b) `SecretKeyPattern_MatchesCompoundCompoundKeyNames`:
  snapshot test pinning regex positives (`Audit:HmacKey`, `Encryption:FieldKey`,
  `EmailHashPepper`, `Module:SecretKey`, `Some:Module:EncryptionKey`) and
  negatives (`ConnectionStrings:Default`, `Hangfire:DashboardPath`,
  `Logging:LogLevel:Default`, the `GetValue<T>` form). (c)
  `AuthServerHost_DoesNotResolve_IAuditHmacKeyResolver`: text-scan of both
  host `Program.cs` for `^\s*[^/].*\.AddAuditInfrastructure\s*\(` — Api MUST
  match; AuthServer MUST NOT (comment-line exempt via `[^/]` first-char).

### Combined doc updates

- `backlogs/backlog.md` header rewritten for the 22:00 P4+P5 SHIPPED state.
- `docs/project-changelog.md` new top entry `v2.137p13` covering both phases.

## What surprised me

- **Word-boundary regex blind spot caught by reviewer.** The initial
  `\b(Hmac|Pepper|EncryptionKey|SecretKey)\b` looks tight but actually fails
  on the exact compound names the guard is supposed to catch — `Audit:HmacKey`
  has no `\b` between `c` and `K`, so the leaked-secret read would pass.
  Fixed inline to substring match with curated alt-list extended with
  `FieldKey` (so `Encryption:FieldKey` is caught). Added a snapshot test so
  future tightening of the regex can’t silently make the guard weaker — this
  matches the Phase-05 plan’s explicit risk-mitigation note.
- **Plan scope vs inventory reality.** Explore-pass turned up 10+ loop-bearing
  seeders not in the plan. Easy to absent-mindedly expand scope. Chose to
  stay strictly within the plan-named 4 (the Vault-pepper consumers), file
  the rest as `P4-FU`, and ship faster — kept this session’s commits small
  and reviewable.
- **`plans/**` is gitignored.** First time noticing in this track; project-manager
  edits stay local and never need staging. Worth remembering for future
  finalize delegations.

## What I deferred

- **`HmacChainServiceVaultIntegrationTests`**, **`OpenIddictDataSeederIsolationTests`**,
  **`EmailHashBackfillServiceTests` isolation case** — all need WebAppFixture
  / SQL Server. Bundled into `P5-FU` to run alongside `P3b` live smoke when
  the dev stack is up. Trade-off: arch guards already prevent the most
  dangerous regression class (direct config reads + AuthServer DI drift).
- **P4-FU** (BtcOrgUnit L2-L4, BtcAssignment, WorkflowTaxonomy, NotificationTemplate,
  SystemParam) — none touch Vault, so risk-weighted backlog.
- Reviewer L-1 (`BackfillResult.Skipped` field — no caller reads it) and
  L-2 (BtcIdentitySeeder summary-log vs `TryFlushAsync` declaration order —
  cosmetic) accepted as P5-FU.

## Verification

- `dotnet build src/AqtCoreFW.slnx`: 0 errors / 0 warnings.
- `AqtCoreFW.Architecture.Tests`: **94 → 97 / 97 PASS** (+3 new).
- `AqtCoreFW.Audit.Infrastructure.Tests.Services.VaultAuditHmacKeyResolverTests`:
  **6 / 6 PASS**.
- `AqtCoreFW.Audit.Infrastructure.Tests`: 20 / 21 PASS — the 1 fail is the
  pre-existing unrelated `AuditCategoryBackfillSqlMirrorTests.Migration_Sql_File_Mentions_Every_Mirror_Key`
  disclosed in v2.137p12.
- `AqtCoreFW.Infrastructure.Extensions.Tests`: 131 / 1-skip PASS.
- `TenantBackfillSeederTests` integration: compiles 0E/0W; live SQL run
  deferred to next dev-stack session.
- Code-review: APPROVE_WITH_NITS (M-1 folded in; L-1/L-2 → P5-FU).

## Carry-over

- `P3b` — live Hangfire 1-job requeue + log-line assert. Needs running stack.
- `P5-FU` — integration tests for HMAC chain + seeder isolation cases.
- `P4-FU` — defensive wraps on the additional 10+ loop seeders.
- Reviewer nits L-1 / L-2.
