# ADR-006: SQL Server 2022 via Docker Desktop for Local Development

**Date:** 2026-04-25
**Status:** Accepted
**Deciders:** AqtCoreFW Development Team

## Context

On **2026-04-18 (v2.114)** we migrated local dev and integration-test SQL Server from Testcontainers-only to a **native Windows install** (`MSSQL$MSSQLSERVER22` @ `localhost,1433`). The stated wins were ~3.4× fixture startup (~12s vs ~41s), stable persistent DBs, and deterministic schema between runs.

After one week of daily use, real-world friction accumulated:

1. **OS lock-in.** Only Windows developers could follow the onboarding path. Any teammate on macOS/Linux had to fall back to Testcontainers, splitting the dev experience.
2. **Manual service lifecycle.** `Start-Service` / `Stop-Service` on `MSSQL$MSSQLSERVER22` is a separate mental model from the rest of the infra, which already runs under Docker (Redis, RabbitMQ, nginx, `authserver`, `api`, `web`).
3. **Version drift vs CI/prod.** The native installer supplied an SKU + patch level that drifted from the CI Testcontainers image (`mcr.microsoft.com/mssql/server:2022-latest`). Hard to reproduce CI bugs locally.
4. **Onboarding blast radius.** New dev = full SQL Server installer + SA password + port config vs. `docker compose up -d sqlserver`.

The existing `infra/docker/docker-compose.yml` already defines a `sqlserver` service (`mcr.microsoft.com/mssql/server:2022-latest`, port 1433, SA auth, healthcheck). Nothing new is required on the infra side.

## Decision

**Revert local dev SQL Server to the Docker Desktop container defined in `infra/docker/docker-compose.yml`.** Hard switch — no `AQT_SQL_MODE=native` fallback.

- Container: `sqlserver` from compose, image `mcr.microsoft.com/mssql/server:2022-latest`, port `localhost,1433`, SA auth (`YourStrong!Passw0rd` — dev only).
- `scripts/1_run-local-dev.ps1` auto-starts the container via `docker compose up -d sqlserver` and waits up to 30s for TCP readiness.
- The script **detects a still-running native `MSSQL$MSSQLSERVER22` service** (which would block port 1433) and prompts the user to stop it before Docker bind.
- Connection string `Server=localhost,1433;...` is unchanged — identical for native and Docker modes, so no app config and no integration-test fixture rewrite is needed.
- Test fixtures continue to use `TEST_USE_LOCAL_SQL=true` as the hybrid toggle; the persistent test DBs (`AqtCoreFW_Test_Main`, `_Messaging`, `_TenantIso`, `_Concurrency`) live inside the Docker container and survive restarts via the `sqlserver_data` volume.

## Consequences

### Positive

- **Unified dev / CI / prod infra.** Same image tag end-to-end. Local bugs reproduce predictably on CI and vice versa.
- **Cross-OS compat.** Windows, macOS, Linux developers now share one path.
- **Simpler onboarding.** Prereq drops from "install SQL Server 2022 Developer Edition + configure named instance" to "install Docker Desktop".
- **Compose parity.** SQL Server is treated like every other infra service (Redis/RabbitMQ/nginx) — one `docker compose up` covers everything.
- **Volume isolation.** `sqlserver_data` keeps dev state across container recreates without polluting the host OS.

### Negative

- **Docker Desktop is now a hard prerequisite.** Already required for Redis/RabbitMQ in practice, so net new friction is negligible.
- **SQL cold start ≈ 15–20s** (empty volume) vs ~2s native service start. Acceptable for dev loop; script pads 30s readiness window.
- **Connection behaviour under heavy fsync load** can be slower on Docker Desktop (Hyper-V / WSL2 file layer) than a bare-metal install. Not measurable at current test volumes.

## Alternatives Considered

| Option | Why Rejected |
|--------|-------------|
| **Keep native install (v2.114)** | OS lock-in + manual lifecycle + version drift (see Context). |
| **Testcontainers only** | Per-test container recreation (~41s) was the original issue that v2.114 tried to solve. Docker container keeps the fixture speed win. |
| **Both modes with env toggle (`AQT_SQL_MODE=native`)** | YAGNI. Extra branch in scripts + docs for zero product benefit. Users who want native can run it themselves; compose stays simple. |
| **Switch to PostgreSQL for dev** | Violates ADR-003 (SQL Server per-module schema) + Vietnamese GOV procurement parity. |

## Implementation Details

- **Script change:** `scripts/1_run-local-dev.ps1` — `Test-SqlServerLocal` replaced with `Test-SqlServerDocker`; call-site updated; docstring revised; docker-infra comment cleaned.
- **Docs sync:** README Quick Start, `developer-onboarding-guide.md`, `deployment-guide.md`, `system-architecture.md`, `codebase-summary.md`, `dependency-map.md` — all narrate Docker container instead of native service.
- **Tracking:** `project-roadmap.md`, `project-changelog.md`, `development-roadmap.md`, `backlogs/backlog.md` bumped to v2.132.
- **Memory:** `sqlserver-docker-dev.md` created; `local-sqlserver-2022.md` marked SUPERSEDED.
- **Journals + ADR-003:** Untouched (immutable history; ADR-003 covers the orthogonal per-module-schema decision).

## Relates To

- **Supersedes (partial):** v2.114 decision, recorded only in `docs/journals/journal-260418-0647-testcontainers-to-local-sql-ship.md` (no prior ADR existed for that change).
- **Orthogonal to:** ADR-003 (per-module schema isolation) — that decision is about database-internal structure, not runtime location, and remains in force.
