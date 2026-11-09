---
title: SRS — Non-Functional Requirements & Compliance
standard: IEEE 830-1998 § 5-6
snapshot_commit: a593d719
snapshot_version: v2.137p113
date: 2026-05-25
language: vi (tech terms EN)
---

# SRS — Non-Functional Requirements & Compliance

> IEEE 830 § 5 (Software Qualities) + § 6 (Other Requirements) for **AqtCoreFW v2.137p113**

---

## Table of Contents

1. [§5.1 Performance Efficiency](#51-performance-efficiency)
2. [§5.2 Security & Compliance](#52-security--compliance)
3. [§5.3 Reliability](#53-reliability)
4. [§5.4 Availability](#54-availability)
5. [§5.5 Usability](#55-usability)
6. [§5.6 Maintainability](#56-maintainability)
7. [§5.7 Portability](#57-portability)
8. [§5.8 Compatibility](#58-compatibility)
9. [§6 Other Requirements](#6-other-requirements)

---

## §5.1 Performance Efficiency

**Ref:** `docs/ops/slo-definitions.md`

### 5.1.1 Time Behavior

| Use Case | Metric | Target | SLO Window |
|----------|--------|--------|-----------|
| API read (GET) | p95 latency | < 200 ms | 5-minute rolling |
| API write (POST/PUT) | p95 latency | < 500 ms | 5-minute rolling |
| **UC-WF-01** (Claim task) | end-to-end latency | < 200 ms | p95 |
| **UC-IDN-01** (Login) | OIDC auth roundtrip | < 1 s | p95 |
| **UC-WF-05** (Inbox load) | read-side query | < 300 ms | p95 |

**NFR-PERF-01:** VỀ lý thuyết, chính sách `p95 < 200ms` cho mọi API routes (trừ health checks + static assets).  
**NFR-PERF-02:** Thêm phương pháp index cơ sở dữ liệu (`WorkflowTaskCandidateIndex`) đảm bảo ngành dọc với throughput ≥500 RPS.

### 5.1.2 Resource Utilization

**NFR-PERF-03:** CPU steady-state < 70% per container during normal load (measured via Prometheus).  
**NFR-PERF-04:** Memory footprint < 2GB per ASP.NET Core process (prod baseline, 8GB host allocation allows 4× instances).  
**NFR-PERF-05:** SQL Server buffer pool tuned via EF Core `QueryTrackingBehavior.NoTracking` on read-side (Dapper via `IReadDbConnection`).

### 5.1.3 Capacity

**NFR-PERF-06:** Concurrent users ≥ 1,000 (peak load target per Law 91/2025 agency baseline).  
**NFR-PERF-07:** Throughput ≥ 500 RPS (recorded in Prometheus as `http_requests_received_total`).  
**NFR-PERF-08:** MinIO storage design target = 10TB cold storage (archival of audit logs + file attachments per NĐ53 12+24 month retention).

---

## §5.2 Security & Compliance

**Ref:** `docs/compliance-evidence-checklist.md`, `docs/runbooks/security-incident-runbook.md`

### 5.2.1 Authentication (NFR-SEC-01..04)

**NFR-SEC-01:** OpenIddict 7.x OIDC server (on-premises, embedded, zero external auth vendor).  
**NFR-SEC-02:** Multi-Factor Authentication (MFA) — TOTP-based (Google Authenticator, Microsoft Authenticator) mandatory for privileged accounts (Ops, Admin, SystemAdmin roles).  
**NFR-SEC-03:** VNeID integration stub (`IVneIdConnector`) with WireMock.Net mock for dev; production requires VN CA provider registration per NĐ59/2023.  
**NFR-SEC-04:** Session management via `ISessionRepository` with sliding token expiry (default 30-minute inactivity timeout).

### 5.2.2 Authorization Layering (NFR-SEC-05..07)

**NFR-SEC-05:** RBAC (Role-Based) — 17 business roles (e.g. Citizen, GovOfficer, FormsMgr, WorkflowApprover) + 2 system roles (Admin, SystemAdmin).  
**NFR-SEC-06:** ABAC (Attribute-Based) — Fine-grained via `AttributeRule` entity (dept, classification level, team-based rules) with `IAttributeRuleReader` evaluation at request pipeline.  
**NFR-SEC-07:** SoD (Separation of Duties) — Conflict matrix prevents simultaneous role assignments (e.g., cannot hold Approver + Requestor on same form class simultaneously).

### 5.2.3 Data Protection (NFR-SEC-08..10)

**NFR-SEC-08:** Always Encrypted (PII fields) — `[Encrypted]` attribute on `IdentityUser` properties (Law 91/2025 personal data minimisation).  
**NFR-SEC-09:** Transport layer — TLS 1.3 mandatory; `HSTS` header + `Strict-Transport-Security: max-age=31536000` on all HTTPS responses.  
**NFR-SEC-10:** At-rest encryption — SQL Server Transparent Data Encryption (TDE) enabled for prod deployments; MinIO server-side encryption (AES-256) on S3 bucket default.

### 5.2.4 Audit & Integrity (NFR-SEC-11..13)

**NFR-SEC-11:** Audit logging — HMAC-chained `AuditLog` (append-only, immutable by design) with `ChainVerificationService` for tamper detection. Log entry includes actor, entity, action, old/new values, timestamp. Ref: `docs/compliance-evidence-checklist.md` § Audit Log Entry Schema.  
**NFR-SEC-12:** Optimistic concurrency via `RowVersion` column; API consumers use `ETag` header + `If-Match` condition to prevent lost updates (HTTP 409 Conflict on conflict).  
**NFR-SEC-13:** Digital signature stub (`IDigitalSignatureService`) — pending external CA provider (MISA/VNPT/Viettel/VGCA) integration per QĐ 742 phase-05b.

### 5.2.5 Cryptography Baseline (NFR-SEC-14)

**NFR-SEC-14:** Password hashing — ASP.NET Core Identity default (PBKDF2 via `PasswordHasher<IdentityUser>`); password minimum length ≥ 12 chars per QĐ742. MFA backup codes encrypted at rest.

### 5.2.6 Vulnerability Management (NFR-SEC-15..17)

**NFR-SEC-15:** SAST (Static Analysis) — SonarQube in CI stage 1; quality gate enforced (blocker: CRITICAL/HIGH findings halt merge).  
**NFR-SEC-16:** DAST (Dynamic Testing) — OWASP ZAP in CI stage 4; zero HIGH findings required.  
**NFR-SEC-17:** SCA (Software Composition Analysis) — `dotnet CycloneDX` SBOM generation + Nexus IQ scan for known vulnerabilities in transitive dependencies. **CVE Pinning:** Scriban 7.2.0+ (from 7.0.6) per GHSA-24c8-4792-22hx (template injection).

### 5.2.7 Vietnamese Regulatory Compliance (NFR-SEC-18..24)

**NFR-SEC-18:** Law 91/2025/QH15 (PDPL) — Personal data handling, consent, RTBF (Right to Erasure) 30-day SLA, breach notification 72-hour SLA. Ref: `docs/compliance-evidence-checklist.md` § Law 91.

**NFR-SEC-19:** NĐ53/2023 (Cybersecurity Decree) — Audit log retention (12+24 months minimum) with `ArchiveAuditLogsJob` yearly archival to MinIO. CI/CD artifacts archived 3 years.

**NFR-SEC-20:** NĐ59/2023 (VNeID Federation) — VNeID as alternative identity provider with verified citizenship claim. Integration pending production setup.

**NFR-SEC-21:** NĐ68/2023 (Digital Signatures) — `IDigitalSignatureService` abstraction for document signing. Real implementation requires external CA provider per NĐ68 + QĐ 742.

**NFR-SEC-22:** NĐ85/TT12 (ATTT Information Security) — SAST + DAST + SCA + penetration testing in CI/CD pipeline (stages 1-5). SBOM generation mandatory.

**NFR-SEC-23:** QĐ742 (Government IT Standard) — Password complexity (uppercase, lowercase, digit, special char), lockout policy (5 failed attempts → 15 min lockout, per-tenant override), login enumeration prevention (canonicalized error messages + timing-channel mitigation), session management, access review (quarterly).

**NFR-SEC-24:** Account lockout policy — ASP.NET Core Identity default (5 attempts, 15-minute lockout) + per-tenant configuration via `CredentialPolicy` (NĐ53). Managed by `TenantAwareUserManager`.

---

## §5.3 Reliability

**Ref:** `docs/ops/slo-definitions.md` § SLO-04

### 5.3.1 Fault Tolerance

**NFR-REL-01:** Retry policy — exponential backoff (base 2, max 5 retries) for transient failures (HTTP 429, 503, timeouts). Implemented via `PollyResiliencePolicy`.  
**NFR-REL-02:** Dead-Letter Queue (DLQ) — Failed async messages routed to `FailedOutboxMessages` table after exhaustion, triggering alerting via Hangfire job monitoring.  
**NFR-REL-03:** Circuit breaker — Open → Half-Open → Closed state machine for external service calls (VNeID, CA providers, SMTP gateway). Threshold: 3 consecutive failures.

### 5.3.2 Outbox Pattern

**NFR-REL-04:** Transactional outbox — Domain events written to `Outbox` table within the same SaveChanges transaction as primary entity changes. Guarantees atomicity: either event + entity both persist, or both rollback.  
**NFR-REL-05:** Exactly-once semantics — Outbox processor (Hangfire background job) reads events, publishes to RabbitMQ, then marks as `ProcessedAt`. Consumer idempotency keys prevent duplicate processing.

---

## §5.4 Availability

### 5.4.1 Uptime SLO

**NFR-AVAIL-01:** Availability target = 99.5% (per `docs/ops/slo-definitions.md` SLO-01). Error budget = 216 minutes/month.  
**NFR-AVAIL-02:** Health check endpoint `/health` returns 200 with liveness probe (database connectivity), readiness probe (message bus, cache), and startup probe (Hangfire storage initialized).

### 5.4.2 Graceful Degradation

**NFR-AVAIL-03:** Cache unavailability (Redis down) — fallback to in-memory `IMemoryCache` (degraded performance, no distributed locking).  
**NFR-AVAIL-04:** Message bus unavailability (RabbitMQ down) — Outbox events queued locally; retry upon bus reconnection (up to 5 retries, then DLQ).

### 5.4.3 Maintenance Windows

**NFR-AVAIL-05:** Planned downtime window = 2 hours/month (pre-announced, maintenance window 02:00-04:00 UTC Sunday). Database migrations use zero-downtime strategy (expand-contract pattern for column renames).

---

## §5.5 Usability

### 5.5.1 Internationalization (i18n)

**NFR-USAB-01:** Default language = Vietnamese (vi). Secondary language = English (en). Locale switching via React context + react-i18next. YARP route `/api/set-culture?culture=en` for server-side claim.  
**NFR-USAB-02:** Liquid template rendering (Forms, workflows) supports both languages via `CultureInfo` injection.

### 5.5.2 Accessibility

**NFR-USAB-03:** React SPA targets WCAG 2.1 Level AA. Audit via Lighthouse CI (target score ≥85 for accessibility).  
**NFR-USAB-04:** Ant Design components configured with semantic HTML (`<label>`, `<button>`, ARIA roles) + keyboard navigation support.

### 5.5.3 Learnability

**NFR-USAB-05:** OpenAPI 3.1 specification auto-generated via Swashbuckle; `/swagger` endpoint available in dev + prod (gated by `Swagger:Enable` config).  
**NFR-USAB-06:** In-app onboarding tooltips + contextual help links in React SPA.

---

## §5.6 Maintainability

**Ref:** `docs/code-standards.md`

### 5.6.1 Code Organization

**NFR-MAINT-01:** Modular monolith — 16 independent modules (Identity, Files, Notifications, Audit, Organization, Forms, Workflow, Reporting, AI, MasterData, SystemParams, Integration, Collaboration, Rules, BusinessProcess, SignalR).  
**NFR-MAINT-02:** Cross-module coupling → SharedKernel only. No circular references. Validated by Architecture tests (`AqtCoreFW.Architecture.Tests`).  
**NFR-MAINT-03:** Per-module file size limit = 200 lines of production code. Modularization triggers on exceed (config, service classes, handlers).

### 5.6.2 Testing Strategy

**NFR-MAINT-04:** Unit test coverage target ≥ 80% (measured via Coverlet). Critical paths (auth, workflow state transitions, permissions) mandatory 100%.  
**NFR-MAINT-05:** Integration tests use Testcontainers (SQL Server, RabbitMQ, MinIO, Redis) or local SQL Server (dev-only via `TEST_USE_LOCAL_SQL=true`).

### 5.6.3 Documentation & Observability

**NFR-MAINT-06:** Structured logging via Serilog; log entries include tenant context, user context, correlation ID (OpenTelemetry trace ID).  
**NFR-MAINT-07:** Distributed tracing — OpenTelemetry instrumentation (Activities, spans) for cross-service calls (ready for Phase 2 microservices).  
**NFR-MAINT-08:** Metrics collection — Prometheus exposition format; Grafana dashboards for request latency, error rate, background job status.

---

## §5.7 Portability

### 5.7.1 Platform Support

**NFR-PORT-01:** Backend runtime = .NET 10 (LTS); runs on Linux (Ubuntu 22.04 LTS), Windows Server 2022, macOS (dev-only).  
**NFR-PORT-02:** Database = SQL Server 2022; alternate `AqtCoreFW_Oracle` repository for Oracle 19c Enterprise (separate deployment model, no cross-repo sync).

### 5.7.2 Containerization

**NFR-PORT-03:** Docker image builds included (`infra/Dockerfile.api`, `infra/Dockerfile.authserver`); published to private registry.  
**NFR-PORT-04:** Kubernetes-ready — Helm charts in `infra/helm/` for managed deployments (resource limits, health probes, rolling updates, horizontal scaling).

---

## §5.8 Compatibility

### 5.8.1 Interoperability

**NFR-COMPAT-01:** REST API — OpenAPI 3.1 specification; JSON payload format; JWT Bearer authentication (`Authorization: Bearer <token>`).  
**NFR-COMPAT-02:** Real-time communication — SignalR 8.x with WebSocket fallback (long-polling if WebSocket unavailable).  
**NFR-COMPAT-03:** Async events — AMQP 0.9.1 via RabbitMQ (internal cross-module); webhook outbound via HTTPS POST with HMAC signature verification.

### 5.8.2 Backward Compatibility

**NFR-COMPAT-04:** API versioning — URL-based (`/api/v1/`, `/api/v2/`) or header-based (`X-API-Version`). Breaking changes require major version bump + deprecation notice (6-month support window).  
**NFR-COMPAT-05:** Database migrations — EF Core migrations preserve schema history; down-migrations not supported (forward-only upgrades).

### 5.8.3 Legacy System Co-Existence

**NFR-COMPAT-06:** YARP reverse proxy gateway (`AqtCoreFW.Gateway`) routes requests to legacy systems when needed (Phase 1 monolith co-exists with Phase 2 satellite services).

---

## §6 Other Requirements

### 6.1 Legal & Compliance

**NFR-LEGAL-01:** All source code, build artifacts, deployment configs follow Vietnamese naming conventions (Vietnamese file paths, Vietnamese comments in critical security/compliance sections; code reserved for EN+tech terms).  
**NFR-LEGAL-02:** Copyright headers in every source file: `// Copyright (c) 2024 AqtCoreFW. All rights reserved. License: AGPL-3.0-or-later.`

### 6.2 Reusability Policy

**NFR-REUSE-01:** Framework components (`IRepository<T>`, `IQueryHandler`, `ICommandHandler`, `IMapper`) exported as NuGet package (`AqtCoreFW.SharedKernel`) for downstream projects (other gov backends).  
**NFR-REUSE-02:** Module-specific business logic (Workflow, Forms, Notifications) remains project-specific; no forced extraction.

### 6.3 Configuration Management

**NFR-CONFIG-01:** Environment-specific settings via `appsettings.json` (dev/staging/prod). Secrets managed via Azure Key Vault (prod) or `secrets.json` (local dev).  
**NFR-CONFIG-02:** Feature flags via `IFeatureManager` (enabled via config, no code changes for beta features).

---

## Summary Statistics

| Category | Count | Notes |
|----------|-------|-------|
| Performance NFRs (NFR-PERF-*) | 8 | Covers latency, throughput, resource utilization |
| Security NFRs (NFR-SEC-*) | 24 | Auth, authz, compliance, CVE management |
| Reliability NFRs (NFR-REL-*) | 5 | Fault tolerance, outbox, DLQ |
| Availability NFRs (NFR-AVAIL-*) | 5 | Uptime SLO, graceful degradation |
| Usability NFRs (NFR-USAB-*) | 6 | i18n, accessibility, learnability |
| Maintainability NFRs (NFR-MAINT-*) | 8 | Code organization, testing, observability |
| Portability NFRs (NFR-PORT-*) | 4 | Platform, containerization |
| Compatibility NFRs (NFR-COMPAT-*) | 6 | Interoperability, versioning, legacy co-existence |
| Legal/Reuse/Config NFRs (NFR-LEGAL/REUSE/CONFIG) | 5 | Governance, reusability, configuration |
| **Total** | **71** | |

---

**Last Updated:** 2026-05-25 (v2.137p113)  
**Maintained by:** AqtCoreFW Documentation Team
