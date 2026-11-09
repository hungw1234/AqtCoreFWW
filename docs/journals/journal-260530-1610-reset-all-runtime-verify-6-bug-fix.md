# Journal — Reset All Mechanism Phase 1: Runtime Verification + 6 Bug Fix

**Date:** 2026-05-30 ~16:10
**Type:** Cook Phase 1 runtime verification (RUN mode, not build-only).
**Commit:** `a75b03bb` main, **pushed origin + bitbucket synced**. 2 files: `scripts/reset-all.ps1` + `docs/dev-data-reset-runbook.md`.
**Status:** Both modes tested real (default DROP + `-Hard` docker down -v), full seeder convergence verified, 6 runtime bugs fixed.

## What

User invoked `/cook "chạy reset-all.ps1 tự động"` — execute the dev-DB reset for real, completing Phase 1 runtime verification deferred in Session 2. Session 2 shipped build+review-only (8.5/10 review, zero runtime). Running it exposed **6 runtime bugs invisible to static review**; all 6 fixed + both modes re-tested.

## The 6 Runtime Bugs

**Bug 1: docker exec container name mismatch**
- Symptom: `docker exec sqlserver` → "No such container".
- Root: `docker ps --filter name=sqlserver` (substring match, passes) but `docker exec` requires exact container name. Compose default = `docker-sqlserver-1` (with hash).
- Fix: NEW `Resolve-SqlContainer` function via `docker ps --format '{{.Names}}'`, caches result, handles rename-safety.

**Bug 2: Auth cold-DB crash (SQL 4060, UNHANDLED)**
- Symptom: AuthServer crashes at startup with `Cannot open database AqtCoreFW` (SQL error 4060), no recovery. Zero exceptions logged (host crash before Serilog init).
- Root: `PlaintextToEncryptedUpgrader` (AuthServer/Program.cs line ~180) + Hangfire `UseSqlServerStorage` autodetect runs at host-build time (BEFORE MigrateAsync creates DB). Both try raw SqlClient on absence → SqlClient caches 4060 in the connection pool → ~5s later the upgrader's call inherits the poisoned pool → crash UNHANDLED.
- Fix: **REORDER bootstrap:** Run `dotnet run --project Api -- --migrate-only` (migrates all 20 DbContexts, creates schema, returns BEFORE Hangfire wiring + PII upgrader runs). Then start Auth + API at runtime. Mirrors Helm db-migrate Job on API image (documented baseline).
- Verification: Restart Auth against pre-existing DB → `/health` 200, zero crash.

**Bug 3: Auth readiness false-timeout (180s cap)**
- Symptom: Script waits for "Now listening on" marker (Session 2 code), AuthServer uses Serilog, never emits that phrase → always timeouts at 180s cap, even on success.
- Root: Log marker assumption (paraphrased from ASP.NET defaults) not verified against AuthServer's actual logging setup.
- Fix: NEW `Wait-HttpOk` HTTP probe on `http://localhost:5002/health` until 200 (+ async host process exit fail-fast). Readiness-agnostic, works for any HTTP service.

**Bug 4: Seeder poll hangs on API crash**
- Symptom: If API crashes mid-seeding, script waits full 900s before timing out.
- Root: Poll loop does NOT check if `dotnet run` host process exited.
- Fix: Add `HasExited` guard in poll loop, fail-fast if host dies.

**Bug 5: migrate-only false-failure (exit code trap)**
- Symptom: `Start-Process -PassThru` with redirected stdout/stderr → `$process.ExitCode` reads `$null` on success. Condition `$null -ne 0` = true → script aborts despite seeder logs "schema migrations complete" + 187 tables created.
- Root: PowerShell `Start-Process -PassThru` quirk: exit code is `$null` when streams redirected; detecting success requires parsing logs + DB state, not exit code alone.
- Fix: Replace `Start-Process -PassThru` with call-operator `&` (inline process, inherits stdout, exit code reliable), use `$LASTEXITCODE`. Log-assertion fallback ("schema migrations complete" + table-count check).

**Bug 6: docker compose down -v cold-start (Win32 10054)**
- Symptom: `-Hard` mode only (default never hit it because DB persists). After `docker compose down -v`, sqlserver container port opens BEFORE engine ready → fresh TCP connects forcibly closed (Win32 error 10054, "connection reset by peer") → migrate-only crashes transiently.
- Root: Port-listening ≠ engine-ready; Windows TCP stack resets before SQL engine binds.
- Fix: NEW `Wait-SqlReady` function, probe `SELECT 1` via `sqlcmd` until success (not just port-open + sleep). Replaces flaky sleep.

## Verification Results

**Default mode (DROP DATABASE):**
- Tenant count: 2 (BTC + BNV)
- Users: 10008 (includes BTC-SEED users)
- OrgUnits: 1143 (BTC tree + BNV ~23-30 + MasterData wards)
- Schema count: 30, Table count: 187

**-Hard mode (docker compose down -v):**
- Tenant count: 2
- Users: 10003 (fewer; demo-seeder+BTC-SEED overlap cleared)
- OrgUnits: 1139
- Full docker rebuild + cold DB proven stable post-fix.

**2-restart convergence** (previously untested highest-risk logic):
- Triggered in BOTH modes: Identity abort "no BTC-SEED users yet" → API restart → assignment seeder resume → second-boot convergence.
- Recovered cleanly both times → proven-correct-in-live.

## Docs Sync

**`docs/dev-data-reset-runbook.md`:**
- Updated migrate-first bootstrap order (new subsection "Cold DB Bootstrap Risk").
- Added health-probe readiness section.
- Noted `pwsh` vs `powershell.exe` (machine has Windows PowerShell 5.1 only, pwsh 7 unavailable).
- Troubleshooting: "4060 Cannot open database" now mapped to migrate-first fix.

**User-level `.claude/skills/aqt-runapp/SKILL.md`** (outside repo, NOT committed):
- Added `pwsh` → `powershell.exe` fallback for `--reseed` entrypoint (skill invokes reset-all.ps1).

## Key Lessons

1. **Runtime > Review (hard proof):** 8.5/10 code review caught 0 of 6 bugs. Static parse + logical walkthrough cannot substitute for executing the destructive path (container ops, network, DB state races, process lifecycle). Session 2's honest build-only status was correct; every bug needed a real run to surface.

2. **Discriminating experiment before redesign:** Bug 2 had 2 candidate theories (cold-DB vs Hangfire autodetect race). Restarted Auth against **existing DB** → `/health` 200, zero crash → cold-DB confirmed. Committed to migrate-first redesign only after empirical evidence settled it (test predicted same outcome for both theories, but DB state unlocked causality).

3. **Exit code ≠ success ≠ failure:** Bug 5 — `Start-Process -PassThru` returns `$null` exit code on success (not 0). Read logs + DB state to disambiguate. Seeder log "schema migrations complete" + table-count grep (187 tables) = ground truth, not `$process.ExitCode`.

4. **Test each mode independently:** `-Hard` exposed Bug 6 (cold-start SQL reset) that default never triggered (default's DB persists from reuse). Don't infer "default passes ⟹ hard passes" — modes stress different paths.

5. **Poll source VERIFICATION before code:** Bug 3 paraphrased "Now listening on" without checking AuthServer's actual logging setup. Lesson from Session 2 (`260529-A`) reinforced: never poll a marker from plan; read source first.

## Process Meta

**Runtime verification as quality gate:** Shipping build+review (Session 2) was the right call (honest status). But every bug needed execution. Code that never runs has 0 confidence. The 2-restart convergence logic (highest risk) is now proven-correct only because we ran it.

**Exit criteria met:**
- Both reset modes execute to completion without hang.
- Seeder convergence markers detected (4 literal markers, sourceable).
- 2-restart abort-recover proven in live.
- Row counts match seeder design (within expected variance).
- Phase 1 complete (ready for P2: BNV tree generalization + 4 sourcing-decision inputs).

## Carry Forward

- PUSH `a75b03bb` done (synced origin + bitbucket).
- Memory file `saturday-260530-cook-phase1-reset-mechanism.md` updated (status = runtime-verified-both-modes, 6-bug list + fixes added).
- Plan phase-01 status = COMPLETE.
- **Unresolved user inputs** (P2/P3 gates): BNV scale finalization (23-30 vs 100-200 plan), HCM/ĐN geography split, special territories, ĐVHC codes. User decision required before P2.
