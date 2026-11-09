# Journal — Audit HMAC + Vault seeder sweep — P1 + P2 + P3a SHIPPED

**Date:** 2026-05-13 21:25 ICT
**Branch:** `main` → pushed `origin/main` `7736b12f`
**Plan:** `plans/260513-1805-audit-hmac-vault-seeder-sweep/`
**Predecessor journal:** [journal-260513-1825-dev-stack-ew-audit-hmac-plan.md](./journal-260513-1825-dev-stack-ew-audit-hmac-plan.md) (discovery + plan filing)

## Session goal

Ship Phase-1 (HMAC resolver), Phase-2 (Vault seed + ops docs), and the offline part of Phase-3 (Hangfire replay docs) of the audit-hmac-vault-seeder-sweep plan. Per-phase commits, single push at the end.

## What worked (verified)

| Commit | Subject | Phase |
|---|---|---|
| `70ac6eaa` | fix(authserver): wrap dev user seeding per-user to prevent single Vault miss from killing host startup | Hot-fix preview of P4 pattern (committed pre-P1 per user choice "Path A: commit hot-fix + start Phase-1") |
| `07b5487a` | feat(audit): source HMAC chain key from Vault via IAuditHmacKeyResolver | P1 |
| `84f89167` | chore(audit): seed aqt/audit/hmac-v1 in Vault dev init and document ops | P2 |
| `7736b12f` | docs(audit): document Hangfire HMAC backlog replay procedure | P3a |

All 4 pushed to `origin/main` `7736b12f`. Working tree clean.

### P1 deliverables (`07b5487a`)
- New `IAuditHmacKeyResolver` interface (Application layer) — `byte[] GetKey()` only, no version param (Option C, single active version)
- New `VaultAuditHmacKeyResolver` (Infrastructure singleton) — path `aqt/audit/hmac-v{Audit:Hmac:CurrentVersion ?? 1}`, base64-32B, 5min sliding cache (Size=1), `Interlocked.CompareExchange`-guarded warn-once legacy fallback (Vault miss → `IConfiguration["Audit:HmacKey"]` Base64-first, UTF-8-second; throws `KeyNotFoundException` if both missing). Length-mismatch `InvalidOperationException` escapes the fallback (real misconfig)
- `HmacChainService` ctor: `IConfiguration` → `IAuditHmacKeyResolver`; `GetKey()` delegates
- DI: `AddSingleton<IAuditHmacKeyResolver, VaultAuditHmacKeyResolver>()` in `AddAuditInfrastructure` before `HmacChainService`
- Build: `dotnet build src/AqtCoreFW.slnx` → 0E / 0W
- Tests: Architecture 94/94, Infrastructure.Extensions 131/1-skip, Audit.Infrastructure 14/15 (1 pre-existing unrelated fail: `AuditCategoryBackfillSqlMirrorTests.Migration_Sql_File_Mentions_Every_Mirror_Key` — migration file consolidated in Phase-06 RLS regen, stale; reproduced on parent commit `70ac6eaa`)
- Test consumer audit: only static `HmacChainService.BuildFingerprint(...)` referenced from tests; no ctor wiring impact; no integration fixture mocks `Audit:HmacKey`

### P2 deliverables (`84f89167`)
- `infra/docker/vault-init.sh`: added `DEV_AUDIT_HMAC_KEY="...B="` (distinct last char from `DEV_AES_KEY="...A="` for grep clarity), `seed "aqt/audit/hmac-v1"` call, removed dead `seed "aqt/encryption/hmac"` line (verified zero source consumers)
- `docs/deployment-guide.md` §5: new row in Vault Secret Paths table for `secret/aqt/audit/hmac-v{N}`; new "Audit HMAC key" subsection covering production seed (`openssl rand -base64 32`), Base64-32B format, `Audit:Hmac:CurrentVersion` config, one-release legacy fallback behavior, rotation deferral rationale + `FU-HMAC-ROTATION` schema-migration pointer, drain-before-swap warning
- `docs/ops/vault-key-rotation-runbook.md`: §2 row replaced (`aqt/encryption/hmac` → `aqt/audit/hmac-v{N}`); §3 "Audit HMAC key" stub added pointing back to deployment-guide
- POSIX sh syntax check: `sh -n` clean

### P3a deliverables (`7736b12f`)
- `docs/deployment-guide.md` §5: new "Recovering from Audit HMAC failures" subsection — discovery SQL (`HangFire.Job WHERE StateName='Failed' AND InvocationData LIKE '%HmacChainService%' ORDER BY Id`), Plan-A (Hangfire dashboard requeue at `/hangfire/failed`, auth via `HangfireAuthorizationFilter` SystemAdmin in prod / open in dev), Plan-B (re-enqueue fallback if `ComputeAsync` signature ever breaks — not needed this release), Plan-C (purge + accept gap, **dev-only**; `VerifyAsync` skip-not-tamper rationale; explicit no-prod warning), post-replay verification query
- Verified `HangfireAuthorizationFilter` exists at `src/shared/AqtCoreFW.Infrastructure/BackgroundJobs/HangfireAuthorizationFilter.cs`, mounted from `Api/Program.cs:628-632`

## What didn't / lessons

- **Mid-session recovery from laptop crash.** Lost shell state; first resume attempt began Phase-2 work assuming Phase-1 was complete based on commit log. User intervened with "Phase 1 chưa xong" — the missing step was the test-consumer audit (verifying no ctor users in tests/fixtures) which was implicit in pre-crash plan. Reverted in-flight Phase-2 edits via `git checkout --`, re-ran the audit (clean: only `HmacChainServiceFingerprintTests` static-method use), then re-did Phase-2 from scratch with proper task tracking. **Lesson:** session-resume checkpoints must explicitly verify both *commit existence* AND *all in-phase verification steps* completed, not just the commit. Recorded as feedback memory pattern.
- **Plan validation prevented two real mistakes** before commit: F1 (early Phase-01 K-2 incorrectly read "AuthServer doesn't register audit module" — actual reason is "AuthServer is Hangfire-CLIENT-only" — wording fixed + Phase-05 negative arch test added) and F2 (runbook row replace vs deprecate — user chose Option-1 replace). Both surfaced by `/ck:plan validate` Full tier.
- **Pre-existing stale test:** `AuditCategoryBackfillSqlMirrorTests.Migration_Sql_File_Mentions_Every_Mirror_Key` references `20260501004056_AddAuditCategoryAndSeverity_M1.cs` which Phase-06 RLS regen consolidated away. Out of plan scope; should be filed as a P3 backlog row (test asserts file existence of a migration no longer present in tree).

## Decisions made

- **Hot-fix commit strategy (Path A vs B).** OpenIddictDataSeeder per-user wrap was discovered in pre-session dev-stack run as an isolated production bug. User chose Path A (commit hot-fix separately as the *pattern reference* before P1) over Path B (defer to P4 sweep). Result: hot-fix shipped early; P4 still does the broader sweep on `BtcIdentitySeeder`, `TenantBackfillSeeder`, `E2ETestDataSeeder`, `EmailHashBackfillService`.
- **P3 split into P3a (offline) + P3b (live).** Phase-3 needs a live API+Vault stack to verify single-job requeue + log-line assert. User accepted offline-first split: P3a (replay docs + Plan-A/B/C runbook) ships now; P3b live smoke deferred to next dev-stack session.
- **Resolver exception filter narrowed.** First draft of `VaultAuditHmacKeyResolver` had `catch when (ex is KeyNotFoundException || ex is not InvalidOperationException)` — convoluted boolean. Simplified to `catch when (ex is not InvalidOperationException)`: KNF still caught (legacy-fallback path); length-mismatch escapes (real misconfig should not silently downgrade to legacy).
- **Vault path namespace.** Deployment-guide §5 already documented `secret/aqtcorefw/encryption/*` but code reads `secret/aqt/encryption/*` (verified in `VaultKeyResolver.cs:64`). New audit row uses code-accurate `secret/aqt/audit/hmac-v{N}`. Wider doc-vs-code drift on the `aqtcorefw/` prefix is out of scope for this plan.

## Test status

| Suite | Pass | Fail | Notes |
|---|---|---|---|
| Build (`src/AqtCoreFW.slnx`) | — | 0E / 0W | full solution |
| Architecture.Tests | 94 | 0 | `Qd742Phase06ComplianceTests` still green (asserts `Audit:HmacKey` placeholder MUST exist in Production config — legacy fallback contract honored) |
| Infrastructure.Extensions.Tests | 131 | 0 (1 skip) | `ProductionSecretValidatorTests` for `Audit:HmacKey` still green |
| Audit.Infrastructure.Tests | 14 | 1 | The 1 failure is pre-existing unrelated stale test |
| Live smoke (Vault seeded, API restart, replay) | — | — | DEFERRED to P3b dev-stack session |

## Next session

1. **P3b live smoke** — bring up dev stack (Auth+API+FE+Vault+SQL); re-run `vault-init.sh` to seed `aqt/audit/hmac-v1`; restart API; requeue 1 representative parked `HmacChainService.ComputeAsync` job via Hangfire dashboard; assert `[INF] HmacChainService: computed HMAC for entry ... seq=...` log line; capture before/after ERR/hr rate
2. **P4 seeder defense sweep** — apply per-user/per-unit `TrySeed*Async` pattern (already shipped for `OpenIddictDataSeeder`) to `BtcIdentitySeeder` (Identity Infra:344), `TenantBackfillSeeder` (Identity Infra:239), `E2ETestDataSeeder` (Api Program.cs:243, Development-only), `EmailHashBackfillService` (Identity Infra:70-74). 4-5h, code-only, parallel-safe with P3b
3. **P5 tests + arch guards** — incl negative arch test `AuthServerHost_DoesNotResolve_IAuditHmacKeyResolver` (guards F1 reasoning correction)
4. **P6 finalize + docs + memory** — single PR-or-direct-to-main decision deferred; per-phase commits already on main; P6 ships changelog patch entry + memory closure
5. **P3 backlog row for stale test** — file `AuditCategoryBackfillSqlMirrorTests.Migration_Sql_File_Mentions_Every_Mirror_Key` as cleanup (out of plan scope; either restore migration file or delete the test)
