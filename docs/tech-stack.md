# AqtCoreFW — Tech Stack

> GOV .NET Core Framework for Vietnamese Government Backends
> Last updated: 2026-04-22 (v2.127p3 — timestamp bump; no tech-stack changes this session)

## Runtime & Framework

| Component | Version | Notes |
|---|---|---|
| .NET SDK/Runtime | 10.0 (LTS) | 3-year LTS cycle |
| ASP.NET Core | 10.x | Rate limiting, HTTPS/HSTS, RequestLocalization built-in |
| EF Core | 10.x | Per-module DbContext, schema isolation, migrations-as-code |
| C# | 14 | Latest language features |

## Architecture Patterns

- **Modular Monolith** (Phase 1) → Microservices via strangler pattern (Phase 2)
- **DDD** — Bounded contexts: Identity, Workflow, Files, Notifications, Integration, Audit, MasterData
- **Clean/Onion Architecture** — Domain → Application → Infrastructure → Presentation
- **CQRS + MediatR** — Command/Query separation with pipeline behaviors
- **Outbox Pattern** — Reliable async events between modules (via MassTransit)
- **API Style** — Controllers (Phase 1 monolith) → Minimal API (Phase 2 satellite services)

## Identity & Auth

| Component | Version | Purpose |
|---|---|---|
| OpenIddict | 7.x | OIDC/OAuth2 server (on-prem, embedded, no external deps) |
| ASP.NET Core Identity | 10.x | User/role store |
| YARP | 2.3 | Reverse proxy / API gateway |
| API Key Authentication | custom | M2M (machine-to-machine) auth scheme |

> Rationale: OpenIddict preferred over Duende (commercial cost) and Keycloak (external infra complexity) for GOV on-prem. Full audit trail embedded in app.

> VNeID: `IVneIdConnector` stub interface + WireMock.Net mock server in Docker Compose for local dev (NĐ59).

## Database & ORM

| Component | Version | Notes |
|---|---|---|
| **SQL Server** | **2022 Developer Edition** | **This repository** — `mcr.microsoft.com/mssql/server:2022-latest` |
| EF Core | 10.x | Write-side ORM; migrations, change tracking, global query filters |
| Dapper | 2.x | Read-side (via `IReadDbConnection` in SharedKernel) — CQRS projections |

> **SQL Server 2022** with Always Encrypted for PII fields (Law 91/2025/QH15 + Decree 356/2025).
> ORM: EF Core (writes + migrations) + Dapper via `IReadDbConnection` interface (reads/reporting).

### Oracle 19c (Separate Repository)

Oracle 19c Enterprise deployments live in [AqtCoreFW_Oracle](https://github.com/thiennd212/AqtCoreFW_Oracle) — a dedicated repository with its own tech-stack doc covering Oracle EF provider, Hangfire job store, TDE + VPD multi-tenant setup. Cross-repo feature porting is decided per-PR; no fixed cadence.

### Multi-tenancy
- Soft multi-tenancy: `org_id` column + `ITenantContext` + EF Core global query filter
- `TenantId` injected via middleware from JWT claim

### Concurrency
- Optimistic Concurrency: `RowVersion` column → `ETag` header → 409 Conflict on conflict


## Cache & State

| Component | Version | Notes |
|---|---|---|
| Redis | 7.x | Session cache, distributed locks (RedLock), pub/sub |
| `ICacheService` | custom | Wraps `IMemoryCache` (dev) → `IDistributedCache`/Redis (prod) via `Cache:Mode` config |
| RedLock | via StackExchange.Redis | Distributed locking for workflow state transitions |
| Idempotency Store | Redis-backed | `Idempotency-Key` header for sensitive operations |

> Redis mode: `Redis:Mode = Standalone | Sentinel | Cluster` via config. `IRedisConnectionFactory` abstraction.

## Object Storage & Files

| Component | Version | Notes |
|---|---|---|
| MinIO | latest | S3-compatible object storage (dev + prod) |
| ClamAV | latest | Virus scanning; `IVirusScanner` interface (swap Metadefender = project-specific) |
| Digital signing (Identity) | custom | USB DocFlow + Viettel Mysign + multi-signer PAdES providers (NĐ68 signing; the old Files `IDigitalSignatureService` fake-pass stub removed 2026-07-23) |

## Async & Background Processing

| Component | Version | Notes |
|---|---|---|
| MassTransit | 8.x | Internal message bus; `IMessageBus` abstraction |
| RabbitMQ | 4.x | Message broker (prod); `Bus:Transport = InMemory \| RabbitMQ` via config |
| Hangfire | 2.x | Background jobs + scheduled jobs; SQL Server job store; `IBackgroundJobService` |

> MassTransit = cross-module domain events (internal async). Webhook Engine = outgoing HTTP to external systems.

### Key Interfaces
- `IMessageBus` — abstract publish/subscribe; MassTransit implementation
- `IBackgroundJobService` — abstract enqueue/schedule/recurring; Hangfire implementation
- `IArchiveService` — data archival to cold storage; Hangfire recurring job (NĐ53)

## Search

| Component | Version | Notes |
|---|---|---|
| SQL Server Full-Text Search | built-in | Default; CONTAINS/FREETEXT; Vietnamese language (LCID 1066) |
| Elasticsearch / OpenSearch | 8.x / 2.x | Optional advanced search adapter |

> `ISearchService` + `ISearchIndexService` in SharedKernel. Mode: `Search:Mode = SqlFts \| Elasticsearch`.
> SQL FTS sufficient for <1M records; ES/OpenSearch for >5M or fuzzy/multi-language needs.

## Webhook Engine

| Component | Notes |
|---|---|
| `IWebhookService` | Outgoing HTTP dispatcher to external systems |
| HMAC-SHA256 | `X-Webhook-Signature` header on outgoing webhooks |
| Hangfire | Retry policy (3 attempts, exponential backoff) |
| `WebhookDeliveryLog` | Audit trail for all webhook dispatch attempts |

## Notifications

| Channel | Technology |
|---|---|
| Email | MailKit (SMTP) |
| SMS | Webhook SMS provider (via `IWebhookSmsProvider`) |
| In-app / Real-time | SignalR |
| Templates | Liquid/Fluid template engine |

## Data Export

| Format | Library |
|---|---|
| Excel (.xlsx) | ClosedXML |
| PDF | QuestPDF |
| Vietnamese fonts | included font resources |

## Secrets & Cryptography

| Component | Version | Purpose |
|---|---|---|
| HashiCorp Vault | 1.21+ | KMS, secrets management, key rotation |
| ASP.NET Core Data Protection | 10.x | Key ring (persisted + encrypted at rest) |
| SQL Server Always Encrypted | built-in | Field-level PII encryption (NĐ13) |

> Vault is **required in dev** via Docker Compose (production parity). No secrets in appsettings.

## Observability & Audit

| Component | Version | Purpose |
|---|---|---|
| OpenTelemetry .NET SDK | 1.15 | Logs + metrics + traces (OTLP export) |
| Serilog | 4.3 | Structured logging |
| Prometheus | 3.x | Metrics TSDB |
| Grafana | 12.x | Dashboards |
| Seq / Loki | latest | Log aggregation (choose per infra) |

> Audit logs: append-only, HMAC-chained, dedicated read-only `AuditDbContext`. Satisfies Law 91/2025/QH15 (PDPL) + NĐ53.
> HTTP logging middleware: configurable request/response logging with PII field masking.

## API Design Standards

- **OpenAPI 3.1.1** — source of truth, contract testing, DAST
- **RFC 9457** — Problem Details error format (with `detail_vi` extension for Vietnamese)
- **RFC 8594** — `Sunset` header for API version retirement + deprecation middleware
- **Idempotency-Key** header — sensitive operations (submissions, payments)
- **URL versioning** — `/api/v{version}/`
- **CORS** — per-environment policy (`Access-Control-Allow-Origin` config-driven)
- **API Key** — auth scheme for M2M calls (`X-Api-Key` header)
- **OWASP API Security Top 10 (2023)** — enforced via middleware + DAST
- **Error Code Catalog** — `GOV_XXX_NNN` constants → RFC 9457 `type` field mapping
- **i18n** — Vietnamese (vi) + English (en); `RequestLocalizationMiddleware` + `.resx`

## Cross-Cutting Infrastructure

| Feature | Implementation |
|---|---|
| Feature Flags | Microsoft.FeatureManagement |
| Resilience | Polly (circuit breaker + retry for external calls) |
| Security Headers | OWASP Headers middleware (CSP, HSTS, X-Frame-Options...) |
| Health Checks | `/health/live` + `/health/ready`; covers SQL, Redis, MinIO, RabbitMQ |
| CORS | Per-environment policy config |
| Rate Limiting | ASP.NET Core built-in |

## VNeID Integration (NĐ59)

- `IVneIdConnector` stub interface in SharedKernel
- WireMock.Net mock server (`vneid-mock` Docker Compose service)
- Mock scenarios: auth success, token expired, locked account, eKYC Level 2/3
- Real connector = project-specific implementation

## MasterData

- Dedicated `MasterData` module (schema: `masterdata`)
- Built-in: Province, District, Ward, CaseType, JobTitle, AdminUnit
- Seeded from embedded JSON resources at startup (idempotent)
- Cached via `ICacheService` (TTL: 1h system / 15m tenant-specific)
- Admin-only CRUD API

## Workflow Engine

- `WorkflowEngine<TState, TAction>` in SharedKernel — generic, configurable
- `IWorkflowDefinition<TState, TAction>` — projects define their own states/transitions
- RedLock for distributed state transition locking
- Workflow module provides golden-path implementation example

## Consent Management (Law 91/2025/QH15 Art. — replaces NĐ13 Art.11)

- `ConsentRecord` entity in Identity module
- Consent API for user opt-in/opt-out
- RTBF command: soft delete → PII field anonymization

## DevSecOps

| Component | Version | Purpose |
|---|---|---|
| GitHub Actions | latest | **Primary CI/CD** — 7-stage pipeline (SAST → Build → Test → DAST → SCA/SBOM → Sign → Deploy). Workflows: `.github/workflows/ci.yml` (.NET) and `ci-frontend.yml` (web), path-filtered (see `deployment-guide.md` section 0). |
| GitLab (Self-managed) | 18.9 | Source mirror (internal) — e.g., `gitlab.aequitas.vn/templates/aqtcorefw.git`. No `.gitlab-ci.yml` present. |
| GitLab SAST/DAST/SCA | managed | Security scanning |
| CycloneDX | latest | SBOM generation (CycloneDX format) |
| Cosign (Sigstore) | latest | Container image signing |
| Docker | latest | Containerization |
| Kubernetes | 1.35 | Orchestration |
| Helm | 3.x | K8s package management (prod charts) |
| Docker Compose | latest | Dev environment (all infra services) |

> CI/CD: SAST → Build → Test → DAST → SCA/SBOM → Sign → Deploy (7 stages).
> DB backup + restore drill scripts (NĐ53 compliance).

## Testing

| Tool | Purpose |
|---|---|
| xUnit | Unit + integration tests |
| Testcontainers | Real DB/cache/MQ containers in integration tests |
| ArchUnit / NetArchTest | Architecture rule enforcement (no cross-module deps) |
| k6 | Load/soak/performance testing (p95/p99 SLO validation) |
| OWASP ZAP | DAST in CI/CD |
| WireMock.Net | VNeID mock + external service stubs |
| Playwright | E2E for portals (if applicable) |

## Compliance Stack Summary

| Regulation | Technical Control |
|---|---|
| **Law 91/2025/QH15 + Decree 356/2025** (PDPL) | Always Encrypted PII + append-only HMAC audit log + RTBF anonymization + Consent Management + BreachNotification 72h workflow. Replaces NĐ13/2023 (expired 2026-01-01). |
| NĐ53 (Cybersecurity) | 12-month log retention + 24-month data retention + backup/DR scripts + IArchiveService |
| NĐ59 (VNeID) | `IVneIdConnector` interface + WireMock.Net dev mock |
| NĐ68 (Digital signatures) | Identity signing stack (USB/Mysign/PAdES); external-CA verification pending phase-05b |
| NĐ85/TT12 (ATTT) | SAST/DAST/SCA pipeline + OWASP pentest checklist + SBOM CycloneDX |
| QĐ742 | Hardening baseline + OWASP Security Headers + account management + audit trail |
