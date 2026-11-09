# Journal — Dev stack E/W triage + Audit HMAC plan filed
**Date:** 2026-05-13 17:42–18:25 ICT
**Branch:** main (uncommitted: 1 file)

## Session goal
Run Auth + API + FE dev stack on `Development` env; surface all E/W; route real bugs to a remediation plan.

## What worked
- **Vault re-seeded** via `vault-init.sh` (was 404 across the board). 6 secrets now present (`aqt/encryption/v1`, `aqt/encryption/hmac`, 3× `email-pepper-*`).
- **AuthServer crash fixed** in-session: patched `OpenIddictDataSeeder.SeedDevUsersAsync` with per-user `TrySeedUserAsync` wrapper (`src/host/AqtCoreFW.AuthServer/OpenIddictDataSeeder.cs`, uncommitted). Build 0E/0W. Vault now seeded → host boots; AuthServer:5002 + API:5001 + FE:5173 all listening; OIDC redirect to AuthServer login works end-to-end.
- **Plan filed** at `plans/260513-1805-audit-hmac-vault-seeder-sweep/` (7 files, ~12-16h effort estimate, frontmatter status=pending, priority P1).
- **Plan validated** via `/ck:plan validate` Full tier sweep — 12 claims checked (9 verified, 2 failed-resolved, 1 unverified-deferred). 4 user decisions recorded in `## Validation Log`.

## What didn't / lessons
- First Auth boot attempt **crashed unhandled** because:
  1. Vault container was healthy but **completely unseeded** (`vault-init.sh` had never been re-run after some Vault state reset).
  2. `OpenIddictDataSeeder.SeedDevUsersAsync` → `EmailHashedUserStore.FindByEmailAsync` → `VaultPepperResolver.GetPepper(Empty)` threw `KeyNotFoundException` which **was not caught** before `Program.Main`.
  This is the exact defense gap flagged in `feedback-vault-init-seed-all-seed-data-tenants.md` — patched now.
- **API host has accumulated Hangfire backlog**: 19 ERR + 8 WRN/hr storm of `Audit:HmacKey is required`. Job IDs `20997..21034` (max-retry) + `40995..41071` (retry 8-9/10). Root cause: `HmacChainService.cs:25` still reads `IConfiguration["Audit:HmacKey"]` directly — Phase-06 Vault Finish migrated AuthServer to `IKeyResolver` but **Audit module was never migrated**. Cross-host config drift.
- **Doc drift**: `vault-key-rotation-runbook.md:36` documents `aqt/encryption/hmac` as the audit chain key — aspirational, never wired. Found via verification grep.

## Decisions made
1. **HMAC rotation**: Option C — path-version `aqt/audit/hmac-v{N}` (ship v1 only); no `HmacKeyVersion` column; rotation deferred (YAGNI).
2. **Hangfire backlog**: REPLAY via dashboard; PURGE fallback if chain order breaks (dev DB zero-compliance).
3. **Seeder defense**: sweep ALL `*Seeder` + `*BackfillService` startup paths (not just `OpenIddictDataSeeder`).
4. **F1 reasoning**: AuthServer DOES register `HangfireAuditJobEnqueuer` (Program.cs:230-231) but NOT `HmacChainService` (Hangfire-CLIENT-only). Phase-05 negative arch test guards future drift.
5. **F2 doc-drift**: replace `vault-key-rotation-runbook.md:36` row (not deprecate-and-add).
6. **Commit strategy**: per-phase commits, single PR (P1→P6).

## Test status
- BE build: 0E/0W (AuthServer host).
- Tests: not run this session (plan validation only).

## E/W taxonomy at session close
- **FATAL**: 0 (fixed: AuthServer KeyNotFoundException).
- **API ERR**: 19 (Hangfire `Audit:HmacKey` retries) — addressed by Phase-01 of new plan.
- **API WRN**: 28 (8 same family + 6 Prometheus no-data + 8 EF sentinel + 3 query-filter shadow + 3 deferred SIGNING/ClamAV/BTC).
- **AuthServer WRN**: 11 (8 EF sentinel + 3 query-filter shadow — same families).
- **FE**: 0 ERR/0 WRN. Console clean. OIDC redirect alive.

## Next session
1. `/ck:cook plans/260513-1805-audit-hmac-vault-seeder-sweep/plan.md` — implement P1 (Audit HMAC resolver) first; commit `OpenIddictDataSeeder` patch with P4 commit per planner phase-04 todo.
2. Verify Hangfire dashboard auth filter in target env (Phase-03 internal step before replay).
3. Deferred: EF sentinel sweep + query-filter shadowing fixes (separate backlog row, low priority).

## Files touched (uncommitted)
- `src/host/AqtCoreFW.AuthServer/OpenIddictDataSeeder.cs` — `TrySeedUserAsync` wrap + `ILogger` import. To be committed as part of Phase-04 per plan.
- 7 new plan files under `plans/260513-1805-audit-hmac-vault-seeder-sweep/`.
