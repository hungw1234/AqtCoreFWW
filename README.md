# AqtCoreFW — GOV .NET Core Framework

> Production-ready .NET 10 backend template for Vietnamese Government projects.
> Built by **AEQUITAS** — clone, extend, ship compliant GOV systems.

## What Is This?

A **template repository** that provides a complete, production-ready foundation for Vietnamese Government backend projects. Teams clone this repo and extend it — no need to set up architecture, compliance controls, or infrastructure from scratch.

**Compliant with:** Law 91/2025/QH15 + Decree 356/2025 (PDPL, replaces NĐ13), NĐ53 (Cybersecurity), NĐ59 (VNeID), NĐ68 (Digital Signatures), NĐ85/TT12 (ATTT), QĐ742.

## Architecture at a Glance

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        AqtCoreFW — Modular Monolith                     │
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │                    Presentation Layer (API)                      │   │
│  │  Controllers (v1) │ YARP Gateway │ OpenAPI 3.1.1 │ Health Checks │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌───────────┐ ┌──────────┐ ┌────────────┐ ┌─────────┐ ┌──────────┐   │
│  │ Identity  │ │ Workflow │ │   Files    │ │  Audit  │ │  Notif.  │   │
│  │ Module    │ │  Module  │ │  Module    │ │  Module │ │  Module  │   │
│  │ OpenIddict│ │ FSM+CQRS │ │ MinIO+ClamAV│ │ HMAC log│ │ Email/SMS│   │
│  └───────────┘ └──────────┘ └────────────┘ └─────────┘ └──────────┘   │
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │                    SharedKernel                                  │   │
│  │  IReadDbConnection │ ICacheService │ ISearchService │ IMessageBus│   │
│  │  IBackgroundJobService │ IWebhookService │ WorkflowEngine<S,A>  │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │                    Infrastructure                                │   │
│  │  SQL Server 2022 │ Redis │ MinIO │ Hangfire │ MassTransit/MQ     │   │
│  │  HashiCorp Vault │ OpenTelemetry │ Serilog │ YARP                │   │
│  └──────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

**Phase 2 path:** Strangler pattern → extract modules as microservices. SharedKernel becomes NuGet packages.

## Database Backend

AqtCoreFW **main repository** runs on **SQL Server 2022** exclusively.

| Repository | Branch | Database | Onboarding Guide |
|------------|--------|----------|------------------|
| **AqtCoreFW** (this repo) | `main` | SQL Server 2022 (Docker) | [`docs/developer-onboarding-guide.md`](docs/developer-onboarding-guide.md) |
| **AqtCoreFW_Oracle** (separate) | `main` | Oracle 19c Enterprise (TDE + VPD) | `AqtCoreFW_Oracle` repo docs |

**Oracle developers:** Clone [AqtCoreFW_Oracle](https://github.com/thiennd212/AqtCoreFW_Oracle) instead — it's a dedicated repository with its own onboarding guide. The two repos are independent; no need to switch branches locally.

The Quick Start below covers SQL Server; see the Oracle repo for Oracle 19c setup.

## Modules

| Module | Description | Compliance |
|--------|-------------|------------|
| **Identity** | OpenIddict OIDC/OAuth2, RBAC+ABAC, VNeID connector stub, Consent Management | NĐ59, NĐ13 Art.11 |
| **Files** | MinIO storage, ClamAV virus scan, self-service multi-provider signature-cert enrollment (Remote Viettel Mysign + USB VGCA) with admin approval + remote single-signer PAdES visible stamp, encryption | NĐ68 |
| **Notifications** | Email (MailKit), SMS (webhook), In-app (SignalR), Liquid templates | - |
| **Audit** | HMAC-chained append-only logs, RTBF anonymization | NĐ13, NĐ53 |
| **Integration** | YARP gateway, Error Code Catalog, Webhook Engine, API Key M2M | - |
| **MasterData** | Province/District/Ward, CaseType (lookup reference), seeded + cached | - |

## Tech Stack

- **Runtime:** .NET 10, C# 14, ASP.NET Core 10
- **ORM:** EF Core 10 (writes) + Dapper via `IReadDbConnection` (reads)
- **Database:** SQL Server 2022 only (Oracle 19c lives in [AqtCoreFW_Oracle](https://github.com/thiennd212/AqtCoreFW_Oracle))
- **Auth:** OpenIddict 7.x (on-prem OIDC), RBAC+ABAC, API Key scheme
- **Cache:** `ICacheService` → IMemoryCache (dev) / Redis (prod)
- **Async:** MassTransit + RabbitMQ (internal events) + Hangfire (jobs) + Webhook Engine (external)
- **Search:** SQL Server FTS (default) + Elasticsearch/OpenSearch (optional)
- **Observability:** OpenTelemetry 1.15 + Serilog 4.3 + Prometheus + Grafana
- **Security:** HashiCorp Vault (secrets), Always Encrypted (PII), OWASP Headers, SAST/DAST

→ Full tech stack: [`docs/tech-stack.md`](docs/tech-stack.md)

## Quick Start

### Prerequisites

- .NET 10 SDK
- Docker + Docker Compose
- Git

### 1. Clone & rename

```bash
git clone https://gitlab.aequitas.vn/templates/aqtcorefw.git MyGovProject
cd MyGovProject
# Replace "AqtCoreFW" with your project name (find & replace in solution)
```

### 2. Start dev infrastructure

**Recommended (local dev):** Use the automation script — SQL Server 2022 runs as Docker Desktop container `sqlserver` (from `infra/docker/docker-compose.yml`, port `1433`), alongside Redis + RabbitMQ. Script auto-starts the container and detects port-1433 conflicts with any native SQL Server service.

```powershell
# Windows
.\scripts\1_run-local-dev.ps1
```

```bash
# Bash/Linux (CI, full-containerised)
docker compose up -d
# Starts: SQL Server 2022, Redis, MinIO, RabbitMQ, Vault, VNeID mock, Seq
```

### 3. Configure secrets

```bash
# Vault is required (production parity) — see docs/deployment-guide.md
vault kv put secret/aqtcorefw \
  ConnectionStrings__Default="Server=localhost;Database=AqtCoreFW;..." \
  Redis__ConnectionString="localhost:6379"
```

### 4. Run migrations

```bash
# Apply all module migrations in correct order
./scripts/migrate-all.sh
```

### 5. Run the API

```bash
dotnet run --project src/host/AqtCoreFW.Api
# API: https://localhost:5001
# API docs (Scalar, dev/staging only): https://localhost:5001/scalar/v1
# OpenAPI spec: https://localhost:5001/openapi/v1.json
# Health: https://localhost:5001/health/ready
# Hangfire: http://localhost:5002/hangfire (AuthServer; requires SystemAdmin login)
```

## Async Processing — Decision Guide

```
Domain event crosses module boundary?  → MassTransit (IMessageBus)
Notify external HTTP endpoint?         → Webhook Engine (IWebhookService)
Scheduled / long-running / retry job?  → Hangfire (IBackgroundJobService)
Sync response to caller?               → MediatR command handler
```

## Project Structure

```
src/
├── shared/
│   ├── AqtCoreFW.SharedKernel/      # Interfaces, base classes, domain primitives
│   └── AqtCoreFW.Infrastructure/    # Cross-cutting implementations
├── modules/
│   ├── identity/
│   ├── workflow/
│   ├── files/
│   ├── notifications/
│   ├── audit/
│   ├── integration/
│   └── masterdata/
└── host/
    └── AqtCoreFW.Api/               # Entry point, DI composition root

tests/
├── unit/
├── integration/
├── architecture/
├── performance/
└── pentest/                         # Pentest evidence template (NĐ85/TT12)

scripts/
├── migrate-all.sh                   # Run all module migrations in order
├── db-backup.sh                     # NĐ53 backup script
└── db-restore-drill.sh              # NĐ53 restore drill

infra/
├── docker-compose.yml               # Dev environment
├── docker-compose.override.yml      # Local overrides
└── helm/                            # Production Kubernetes charts
```

## Adding a New Module

```bash
dotnet new aqt-module --name MyModule --output src/modules/mymodule
```

→ See [`docs/guides/module-creation-guide.md`](docs/guides/module-creation-guide.md) for step-by-step guide.

## Documentation

| Doc | Description |
|-----|-------------|
| [`docs/tech-stack.md`](docs/tech-stack.md) | Full tech stack with versions and rationale |
| [`docs/system-architecture.md`](docs/system-architecture.md) | Architecture deep-dive |
| [`docs/code-standards.md`](docs/code-standards.md) | Coding standards and conventions |
| [`docs/deployment-guide.md`](docs/deployment-guide.md) | Docker Compose + Kubernetes deployment |
| [`docs/project-roadmap.md`](docs/project-roadmap.md) | Development phases and roadmap |

## Compliance Checklist

Before deploying to GOV environment, verify:

- [ ] Law 91/2025/QH15 + Decree 356/2025: PII fields encrypted (Always Encrypted), consent records present, RTBF implemented, breach notification 72h workflow
- [ ] NĐ53: Audit log retention configured (12 months online, 24 months archive), backup drill done
- [ ] NĐ59: VNeID connector implemented (replace stub), eKYC level configured
- [ ] NĐ68: External VN CA provider integrated (MISA/VNPT/Viettel/VGCA — replaces Files module stub), CRL/OCSP delegated to provider
- [ ] NĐ85/TT12: SAST/DAST passed, pentest evidence documented (`tests/pentest/`)
- [ ] QĐ742: Security headers enabled, account management configured, audit trail complete

---

> **AEQUITAS Internal** — Not for public distribution.
