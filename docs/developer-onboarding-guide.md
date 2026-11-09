# Developer Onboarding Guide

Welcome to AqtCoreFW — a production-ready .NET 10 backend template for Vietnamese Government projects. This guide gets you from zero to a running local environment.

> **This guide covers SQL Server 2022.** 
>
> If you need Oracle 19c Enterprise instead, clone the dedicated repository [AqtCoreFW_Oracle](https://github.com/thiennd212/AqtCoreFW_Oracle) — it has its own setup guide. The two repositories are independent and don't share branches locally.

---

## Prerequisites

| Tool | Version | Install |
|---|---|---|
| .NET SDK | 10.0+ | https://dotnet.microsoft.com/download |
| Docker Desktop | 4.x+ | https://www.docker.com/products/docker-desktop |
| Git | 2.x+ | https://git-scm.com |
| IDE | VS Code or JetBrains Rider | — |
| psql (optional) | any | For DB debugging |

**VS Code extensions:** C# Dev Kit, REST Client, Docker.
**Rider plugins:** .NET Core User Secrets, Ideolog.

---

## 1. Clone & Restore

```bash
git clone https://github.com/aequitas/AqtCoreFW.git
cd AqtCoreFW/src
dotnet restore AqtCoreFW.slnx
```

---

## 2. Start Infrastructure

### 2.1 Database — SQL Server 2022 (Docker Desktop)

**Development environment:** SQL Server 2022 runs as a Docker Desktop container named `sqlserver` (image `mcr.microsoft.com/mssql/server:2022-latest`) from `infra/docker/docker-compose.yml`, bound to `localhost,1433` with SA authentication. See ADR 006.

**Verify connection (after container is up):**
```bash
sqlcmd -S localhost,1433 -U sa -P 'YourStrong!Passw0rd' -Q "SELECT @@VERSION;"
```

**Configure connection string** (matches the compose defaults; override only if you customized the SA password):
```bash
cd src/host/AqtCoreFW.Api
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,1433;Database=AqtCoreFW;User Id=sa;Password=YourStrong!Passw0rd;Encrypt=false;TrustServerCertificate=true;"
```

### 2.2 Other Services — Docker Compose

All infrastructure (SQL Server + Redis + RabbitMQ) runs containerized. For local dev, the recommended entrypoint is `scripts/1_run-local-dev.ps1`, which auto-starts the `sqlserver`, `redis`, and `rabbitmq` containers; app services still run natively via `dotnet run`.

```powershell
# Recommended — local dev automation (Windows)
.\scripts\1_run-local-dev.ps1
```

```bash
# Manual / full-container mode (CI, Linux)
cd infra
docker compose up -d
```

Services started:

| Service | Port | Purpose |
|---|---|---|
| Redis | 6379 | Cache + SignalR backplane |
| MinIO | 9000 / 9001 | Object storage (files, exports) |
| RabbitMQ | 5672 / 15672 | Async messaging (MassTransit) |
| HashiCorp Vault | 8200 | Secrets management |
| VNeID mock | 8300 | NĐ59 VNeID integration stub |
| Seq | 5341 / 80 | Structured log viewer |

---

## 3. Configure Secrets (Vault)

Vault is **required** in dev — no plaintext secrets in `appsettings.json`.

```bash
# Initialise Vault with dev secrets (idempotent)
cd infra
./scripts/vault-init-dev.sh
```

This seeds:
- SQL Server connection string
- Redis connection string
- MinIO access/secret key
- RabbitMQ credentials
- JWT signing key

Verify Vault is running: http://localhost:8200 (token: `dev-root-token`)

### 3.1. Audit HMAC Chain Key

`HmacChainService` requires `Audit:HmacKey` — not currently seeded by `vault-init-dev.sh`. Without it the Hangfire job `ComputeHmacChainJob` throws `InvalidOperationException` on every audit entry and burns its 10-retry budget.

Seed via .NET user-secrets (UserSecretsId `aqtcorefw-api-dev`):

```bash
cd src/host/AqtCoreFW.Api
# 64 hex chars = 32 random bytes when read as UTF8
KEY=$(openssl rand -hex 32)
dotnet user-secrets set "Audit:HmacKey" "$KEY"
```

Rotating the key invalidates the entire existing chain — `VerifyAsync` will recompute from seq 0 against the new key and report mismatches for historical entries. For Production, source via Vault / env var `Audit__HmacKey` instead.

---

## 4. Apply Database Migrations

```bash
cd src

# Apply all module DbContexts
dotnet ef database update \
  --project modules/identity/AqtCoreFW.Identity.Infrastructure \
  --startup-project host/AqtCoreFW.Api \
  --context IdentityDbContext

dotnet ef database update \
  --project modules/workflow/AqtCoreFW.Workflow.Infrastructure \
  --startup-project host/AqtCoreFW.Api \
  --context WorkflowDbContext

# Repeat for: Audit, Files, Notifications, Organization, Forms, MasterData
```

MasterData seeding (provinces/districts/wards) runs automatically on first startup.

---

## 5. First Run

```bash
# Terminal 1 — Auth Server (OIDC/OAuth2)
cd src/host/AqtCoreFW.AuthServer
dotnet run

# Terminal 2 — API
cd src/host/AqtCoreFW.Api
dotnet run
```

API: https://localhost:7001
AuthServer: https://localhost:7000
Scalar API Docs: https://localhost:7001/scalar/v1 (dev/staging only)
Hangfire dashboard: http://localhost:5002/hangfire (AuthServer; requires SystemAdmin login)

---

## 6. Environment Configuration Reference

Key `appsettings.Development.json` entries (non-secret):

```json
{
  "Vault": {
    "Enabled": true,
    "Address": "http://localhost:8200",
    "Token": "dev-root-token"
  },
  "MessageBus": {
    "Transport": "RabbitMQ",
    "Host": "localhost"
  },
  "Telemetry": {
    "OtlpEndpoint": "http://localhost:4317"
  },
  "Serilog": {
    "WriteTo": [{ "Name": "Seq", "Args": { "serverUrl": "http://localhost:80" } }]
  }
}
```

Secrets (loaded from Vault at startup, never in files):
- `ConnectionStrings:Default`
- `ConnectionStrings:Redis`
- `Jwt:SigningKey`
- `Minio:AccessKey` / `Minio:SecretKey`

---

## 7. Common Development Tasks

### Add a new module
Follow `docs/guides/module-creation-guide.md` step-by-step.

### Add a migration
```bash
dotnet ef migrations add {MigrationName} \
  --project modules/{module}/AqtCoreFW.{Module}.Infrastructure \
  --startup-project host/AqtCoreFW.Api \
  --context {Module}DbContext
```

### Run tests
```bash
# Unit tests only (fast — no containers)
dotnet test tests/unit/ --logger trx

# Integration tests (requires Docker — pulls SQL Server image)
dotnet test tests/integration/ --logger trx

# Architecture tests
dotnet test tests/architecture/ --logger trx

# All tests with coverage
dotnet test AqtCoreFW.slnx \
  --collect:"XPlat Code Coverage" \
  --results-directory ./coverage
```

### View logs
Seq UI: http://localhost:80
Filter by correlation ID: `CorrelationId = "abc-123"`

### View background jobs
Hangfire: http://localhost:5002/hangfire (AuthServer; log in as SystemAdmin)
RabbitMQ management: http://localhost:15672 (guest/guest)

**Hangfire Dashboard:** The Hangfire dashboard is hosted on the **AuthServer** at `/hangfire`. A SystemAdmin login is required in **all environments** (dev, staging, prod) — there is no dev-open bypass. Use a seeded dev SystemAdmin account to log in and view scheduled jobs, requeue failed jobs, or manually trigger background work.

---

## 8. Architecture Overview

| Doc | Purpose |
|---|---|
| `docs/system-architecture.md` | Layer model, module map, request flow |
| `docs/code-standards.md` | Naming, CQRS, error handling patterns |
| `docs/guides/module-creation-guide.md` | Step-by-step new module guide |
| `docs/adr/` | Architectural Decision Records |
| `src/docs/api-design-standards.md` | URL conventions, pagination, versioning |
| `src/docs/tech-stack.md` | Full dependency list with versions |

---

## 9. Debugging Tips

**CORS errors:** Add your frontend origin to `GovApiPolicy` in `AddInfrastructure`.

**401 Unauthorized:** Check that the AuthServer is running and the JWT `issuer` in `appsettings` matches.

**EF Core migrations fail:** Ensure SQL Server (`localhost,1433`) is accessible; test with `sqlcmd -S localhost,1433 -U sa -Q "SELECT 1"`. Verify connection string in user-secrets via `dotnet user-secrets list`. Check Vault health if fallback to env vars.

**MassTransit consumer not firing:** Check RabbitMQ management UI — confirm the exchange and queue exist. Restart the API if they are missing (registered on startup).

**Vault secrets not loading:** Confirm `vault-init-dev.sh` ran successfully. Check `dotnet run` output for Vault connection errors.

**Hangfire jobs not running:** Hangfire requires the `hangfire` SQL Server schema. Re-run the Hangfire migration if it is missing.
