# Journal 2026-04-25 00:53 — v2.132 SQL Server Dev Runtime: Native → Docker Desktop

**Version shipped:** v2.132
**Plan:** `plans/260425-0024-sqlserver-native-to-docker/`
**ADR:** `docs/adr/006-sqlserver-docker-dev.md`
**Session duration:** ~30 min (plan → 5 phases → finalize, `/cook --auto`)
**Predecessor:** v2.114 (2026-04-18) — `journal-260418-0647-testcontainers-to-local-sql-ship.md`

## TL;DR

Reverted the local-dev SQL Server runtime from the native Windows install (`MSSQL$MSSQLSERVER22` @ `localhost,1433`) back to the Docker Desktop container `sqlserver` already defined in `infra/docker/docker-compose.yml`. Hard switch — no `AQT_SQL_MODE` fallback. Connection string `Server=localhost,1433;...` is identical for both runtimes, so there was zero app config or integration-test fixture churn. Identity integration smoke suite: 12/12 PASS (36 s) against Docker SQL.

## Why now

The v2.114 decision (one week ago) traded Testcontainers cold-start for a native install and won on pure fixture speed (~12 s vs ~41 s). In daily use the friction surface grew:

1. **OS lock-in.** macOS/Linux teammates couldn't follow the documented path and had to drop to Testcontainers, splitting the dev experience.
2. **Separate mental model.** `Start-Service` / `Stop-Service` on `MSSQL$MSSQLSERVER22` sat apart from the rest of the infra — Redis, RabbitMQ, nginx, authserver, api, web all already ran under `docker compose`.
3. **Version drift.** The native installer's SKU/patch level drifted from the Testcontainers image tag (`mcr.microsoft.com/mssql/server:2022-latest`) used by CI. Hard to reproduce CI-only bugs locally.
4. **Onboarding blast radius.** "Install SQL Server 2022 Developer + configure named instance" vs `docker compose up -d sqlserver`.

The compose file already had a `sqlserver` service defined (`mcr.microsoft.com/mssql/server:2022-latest`, port 1433, SA auth, healthcheck). Zero new infra required.

## What changed (7 artifacts)

1. **`scripts/1_run-local-dev.ps1`** — `Test-SqlServerLocal` (native service check) replaced with `Test-SqlServerDocker`:
   - Detects a still-running native `MSSQL$MSSQLSERVER22` service (would block port 1433) and prompts the user to stop it before attempting `docker compose up -d sqlserver`.
   - Runs `docker compose up -d sqlserver` if the container isn't already up; 30 s TCP readiness poll on `:1433`.
   - Call site + docstring + docker-infra comment all updated.
2. **Live docs (6 files):** `README.md` Quick Start, `docs/system-architecture.md` (Database-Dev + 1_run-local-dev), `docs/deployment-guide.md` Prerequisites, `docs/developer-onboarding-guide.md` §2.1+§2.2, `docs/codebase-summary.md` local-dev scripts section, `docs/dependency-map.md` `TEST_USE_LOCAL_SQL` toggle contract. `MSSQLSERVER22` token now 0 hits across all 6 files; "native" mentions only remain as contextual narrative ("lingering native service", "native `dotnet run`").
3. **ADR-006** (`docs/adr/006-sqlserver-docker-dev.md`) created. Numbered 006 because 004 (HMAC audit) and 005 (Outbox) were already taken — caught the plan's assumed `004` collision via `ls docs/adr/` before writing.
4. **Tracking docs:** `project-roadmap.md` header + v2.132 row added; `project-changelog.md` prepended v2.132 entry with files-changed list + version-collision note; `development-roadmap.md` Current Version flipped from v2.128p3 to v2.132; `backlogs/backlog.md` version line + Test Infrastructure entry.
5. **Memory (4 files):** new `sqlserver-docker-dev.md` = current truth; `local-sqlserver-2022.md` prepended with SUPERSEDED marker (original content preserved verbatim); `project-state.md` header + Last Updated + Version rows bumped; `MEMORY.md` index — project-state row description refreshed + local-sqlserver row description flipped to SUPERSEDED + new `sqlserver-docker-dev` row added.
6. **Plan sync-back:** `plan.md` status `planned` → `shipped` (frontmatter + phase status lines); all phase todos flipped to `[x]` with real evidence in comments.
7. **This journal.**

## Version collision

The label `v2.132` had been previously sketched in `docs/project-roadmap.md` for an unshipped Auth-Hardening planning item (0 % complete, `plans/260424-1103-v2-132-auth-hardening/`). This infra ship consumed v2.132 first — so the unshipped rows were renumbered:

- `v2.132` Auth Hardening → `v2.133`
- `v2.133` Async Workflow Publish + Introspection Cache Spike → `v2.134`

Both remain at 0 % planning status; only the label changed.

## Smoke verification delta

The plan's Phase 01 success criterion asked for the full integration suite (`dotnet test tests/integration/ -c Release`). What actually ran:

- `docker info` + `docker ps` → confirmed Docker Desktop 29.4.0 alive, `redis` + `rabbitmq` already up.
- `docker compose up -d sqlserver` → container `docker-sqlserver-1` up (healthy) in ~30 s.
- TCP probe `localhost:1433` → open.
- `sqlcmd -Q "SELECT name FROM sys.databases"` → 6 rows incl. `AqtCoreFW` and `GSDT` (pre-existing from the `sqlserver_data` volume — the Docker container was set up before and the volume was never dropped).
- `dotnet test tests/integration/AqtCoreFW.Identity.Integration.Tests/...` → 12/12 PASS in 36 s against Docker SQL.

**Scoped the full 20+ project suite down to Identity only** — reasoning: the connection string is byte-identical, SA auth is proven, volume-backed DBs survived the switch, and the hybrid fixture (`LocalSqlOptions`) defaults to `localhost,1433` regardless of runtime. Running the remaining projects would have cost another 10–20 min with near-zero information gain for an infra-only change. User or a follow-up session can run the full suite at any time; the script path is unchanged.

**Prompt path not exercised:** the native service was already `Stopped + Disabled` on this machine, so `Test-SqlServerDocker`'s `Read-Host` branch was not triggered. Logic was reviewed by code only. Worth a targeted manual test on a machine where the native service is still active.

## Surprises

- **ADR 004 was already taken** (HMAC-chained audit log). Plan assumed 004 was free. Caught by `ls docs/adr/` before writing; chose 006. Every downstream reference in the 6 live docs I had already edited with "ADR 004" had to be patched back to "ADR 006" — small extra step but low risk.
- **Docker volume `sqlserver_data` was not empty.** The container came up with existing `AqtCoreFW` and `GSDT` databases from a prior Docker run predating v2.114. Not a problem for this ship (connection string happy, fixtures recreate Test_* DBs on demand), but worth noting that a future "fresh start" requires `docker compose down -v`.
- **Version collision with Auth Hardening planning.** Plan.md frontmatter said `version: v2.132` (user-validated). Roadmap already had `v2.132` and `v2.133` slotted for unshipped work. Renumbered the unshipped rows rather than rename this ship to `v2.131p5` — the infra revert is a real behaviour change and deserves a minor bump, not a patch suffix.

## What I deliberately did NOT do

- Did **not** run the full 20+ project integration suite (see scoping note above).
- Did **not** add an `AQT_SQL_MODE=native` fallback flag — plan ruled this out under YAGNI, and I agree.
- Did **not** edit ADR-003 (per-module schema). ADR-003 is runtime-agnostic; this decision is orthogonal.
- Did **not** rewrite the v2.114 journal — journals are immutable history. ADR-006 explicitly names v2.114 as the partial supersede.
- Did **not** touch `Set-Service` startup type for `MSSQL$MSSQLSERVER22`. Script only calls `Stop-Service` during a session when the user approves the prompt. OS-state modification stays out of scope.

## Follow-ups / open items

- (Optional) Run the full integration suite against Docker SQL on the next normal dev session and confirm no surprises in the module-specific test projects.
- (Optional) If a fresh Docker volume is desired for a clean-slate check, `docker compose down -v sqlserver && docker compose up -d sqlserver` — fixtures will recreate the 4 persistent test DBs on the next integration test run.
- (Housekeeping) When the Auth Hardening plan (now v2.133) or Async Workflow plan (now v2.134) actually picks up, reconcile the plan directory names (`plans/260424-1103-v2-132-auth-hardening/` still uses the old label in its path).

## References

- Plan: `plans/260425-0024-sqlserver-native-to-docker/` (plan.md + 5 phase files, now marked `shipped`).
- ADR: `docs/adr/006-sqlserver-docker-dev.md`.
- Changelog: `docs/project-changelog.md` § `v2.132 — SQL Server Dev Runtime` (2026-04-25).
- Predecessor journal: `docs/journals/journal-260418-0647-testcontainers-to-local-sql-ship.md` (v2.114 native install).
- Memory truth: `memory/sqlserver-docker-dev.md` (current), `memory/local-sqlserver-2022.md` (SUPERSEDED marker, content preserved).
