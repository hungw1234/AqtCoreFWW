# Project Overview & Product Development Requirements (PDR)

## Executive Summary

**AqtCoreFW** is a production-ready .NET 10 backend template (modular monolith) designed for rapid deployment of Vietnamese government projects. It provides a complete, compliant foundation with built-in support for security (NĐ59, NĐ85/TT12), privacy (Law 91/Decree 356), audit (NĐ53), and digital signatures (NĐ68).

**Status:** v2.137-QD742-PHASE06 COMPLETE — Full-Stack Platform with QĐ 742 Phase-01/02/03/04/06 Compliance (2026-05-10)

**Completion Summary (2026-05-10):**
- **All 15 backend modules COMPLETE** — fully implemented and tested (added Collaboration, Rules; removed Search, Cases, Signature)
- **37+ frontend pages COMPLETE** — all wired to real APIs via orval SDK (32 modules)
- **BE tests:** 2,234+ (1,822 unit + 25 integration projects + 119 T5-10 migration tests) | **FE tests:** 559+ vitest
- **V2 features:** dark mode, profile, toasts, role mgmt, field reorder, workflow steps, batch cases, announcements, session timeout, audit CSV, error boundary, health dashboard, i18n VI/EN, JIT SSO, dynamic workflow (7 phases), form builder
- **Security audit:** 30/30 findings remediated (100%) — Groups A-F complete
- **Production ready:** CI/CD green, Vault key rotation (Phase-06), Docker hardened, Helm production-grade, AuthServer Helm chart, RLS enforcement
- **QĐ 742 Compliance:** Phase-01/02/03/04/06 shipped (~80% of 41 ReqIDs covered)

---

## Functional Requirements

### FR-001: User Identity & Authentication
- Requirement: Support OIDC/OAuth2 authentication via OpenIddict 7.x
- Acceptance Criteria:
  - Users can authenticate via authorization code flow
  - JWT access tokens issued with claims: `sub`, `tenant_id`, `roles`, `dept_code`
  - Token introspection endpoint validates tokens for API calls
  - Refresh token flow supported
- Implementation Status: **COMPLETE** (Identity module, Phase 04)
- Evidence: OpenIddict configured in `AqtCoreFW.AuthServer`, integration tests passing

### FR-002: Multi-Factor Authentication
- Requirement: Support TOTP-based MFA per QĐ742 guidelines
- Acceptance Criteria:
  - Users can enable/disable MFA via AuthServer
  - QR code generation for authenticator apps (Google Authenticator, Microsoft Authenticator)
  - Time-based OTP validation on login
  - Backup codes provided on MFA setup
- Implementation Status: **COMPLETE** (IMfaService in Application layer)
- Evidence: `IMfaService` interface in AqtCoreFW.Identity.Application, handlers for setup/validation

### FR-003: Role-Based Access Control (RBAC)
- Requirement: Assign roles to users, validate role claims on protected endpoints
- Acceptance Criteria:
  - Roles stored in OpenIddict claims
  - Controllers can authorize by role: `[Authorize(Roles = "Admin,Operator")]`
  - Role hierarchy: Admin ⊇ Supervisor ⊇ Operator ⊇ Viewer
  - API returns 403 Forbidden if role missing
- Implementation Status: **COMPLETE** (OpenIddict claims mapping)
- Evidence: JWT claims include `roles` array, tested in integration tests

### FR-004: Attribute-Based Access Control (ABAC)
- Requirement: Fine-grained authorization based on user attributes (department, classification level)
- Acceptance Criteria:
  - Attribute rules stored in database (IAttributeRuleRepository)
  - Authorization handler evaluates user attributes against resource attributes
  - Deny access if department mismatch or classification level insufficient
  - Audit log records authorization decisions
- Implementation Status: **COMPLETE** (IAttributeRuleRepository, AbacAuthorizationHandler)
- Evidence: Repository interface in Domain, implementation in Infrastructure, handler in Application

### FR-005: Consent Management (GDPR/PDPL)
- Requirement: Track user consent for data processing per Law 91/Decree 356 Article 11
- Acceptance Criteria:
  - Users can grant/withdraw consent for specific data processing purposes
  - Consent records timestamped and immutable (append-only)
  - GrantConsentCommand and WithdrawConsentCommand available
  - Audit trail shows who granted/withdrew when
- Implementation Status: **COMPLETE** (IConsentRepository, grant/withdraw handlers)
- Evidence: Consent aggregate with DomainEvent, handlers depend on repository interface

### FR-006: Role Delegation
- Requirement: Authorized users can delegate roles to other users temporarily
- Acceptance Criteria:
  - Delegation stored with effective date range (from, until)
  - DelegateRoleCommand and RevokeDelegationCommand available
  - Expired delegations automatically excluded from authorization
  - Audit log shows delegation chain
- Implementation Status: **COMPLETE** (IDelegationRepository, delegate/revoke handlers)
- Evidence: Delegation aggregate, handlers in Application layer

### FR-007: VNeID Federation
- Requirement: Integrate with Vietnam's national e-ID (VNeID) per NĐ59
- Acceptance Criteria:
  - VNeID connector stub present (replace with real integration)
  - eKYC level configuration (1 = ID card, 2 = FA, 3 = video)
  - User attributes imported from VNeID: name, DOB, address, ID number
  - Audit log tracks VNeID authentication events
- Implementation Status: **STUB** (AqtCoreFW.Identity.Presentation/VNeIdController.cs)
- Evidence: Placeholder implementation present, requires replacement with real VNeID OAuth endpoint

---

## Non-Functional Requirements

### NFR-001: Security
- Requirement: Implement OWASP Top 10 and Vietnamese government standards
- Acceptance Criteria:
  - TLS 1.2+ enforced on all endpoints
  - Security headers (CSP, HSTS, X-Frame-Options, X-Content-Type-Options)
  - SQL injection prevention (parameterized queries via EF Core + Dapper)
  - CSRF protection via anti-forgery tokens
  - API rate limiting (10 req/sec per IP by default)
  - Secrets stored in HashiCorp Vault (no plaintext in config)
- Standards Alignment: OWASP, NĐ85/TT12, NĐ53
- Implementation Status: **COMPLETE** (SecurityHeadersMiddleware, JWT validation, rate limiting)
- Evidence: Middleware stack in Program.cs, tested in architecture tests

### NFR-002: Data Privacy
- Requirement: Protect personally identifiable information (PII) per Law 91/Decree 356
- Acceptance Criteria:
  - PII columns encrypted at rest using AES-256-GCM via Vault
  - Column-level encryption keys managed via Vault (IKeyResolver, v{N}: prefix versioning)
  - Right-to-be-forgotten (RTBF) implemented (anonymization, not deletion)
  - Consent records immutable and audit-logged
  - Data breach notification workflow (72-hour timeline)
  - Data classification (Public/Internal/Confidential/Restricted) enforced via RLS
- Implementation Status: **COMPLETE (Phase-04/Phase-06)** (Encryption at rest + key rotation, RTBF anonymization live with full test coverage, classification guards in place)
- Evidence: Phase-04 (2026-05-10): ApplicationUser Email/NormalizedEmail/PhoneNumber encrypted. RtbfRequest SubjectEmail/CitizenNationalId encrypted. ClassificationRule entity with 4-column EmailHash split (global + per-tenant pepper). Phase-06 (2026-05-10): IKeyResolver abstraction, VaultKeyResolver (aqt/encryption/v{N} mount), EncryptedStringConverter dual-decode + v{N}: prefix, LegacyCipherUpgrader for backfill, PlaintextToEncryptedUpgrader for one-time upgrade. Vault key rotation runbook shipped. 43/43 architecture tests PASS.

### NFR-003: Audit Logging
- Requirement: Comprehensive audit trail per NĐ53
- Acceptance Criteria:
  - All data mutations logged with: actor, timestamp, operation (CRUD), before/after values
  - Audit logs immutable and append-only (HMAC-chained)
  - Retention: 12 months online, 24 months in archive
  - Searchable by actor, entity, timestamp, operation
  - Real-time alerts on sensitive operations (admin login, role grant, etc.)
- Implementation Status: **COMPLETE** (Audit module with HMAC-chained logs)
- Evidence: AuditLog aggregate, MassTransit consumers subscribe to domain events, queryable via IReadDbConnection

### NFR-004: Performance
- Requirement: API response time <500ms (p95) for typical operations
- Acceptance Criteria:
  - Read-side queries optimized (Dapper + SQL indexing)
  - Caching strategy: Redis for hot data (roles, masterdata, search results)
  - No N+1 queries (Dapper, not ORM for reads)
  - Connection pooling configured
  - Query timeouts enforced (30 sec default)
  - k6 load testing for all modules (load, spike, soak profiles)
- Implementation Status: **COMPLETE** (Dapper for reads, Redis ICacheService, EF Core query filters, k6 test suite with module-specific tests)
- Evidence: IReadDbConnection abstraction, ICacheService decorator on query handlers, shared k6 libs with API Key auth, 4 module load tests

### NFR-005: High Availability
- Requirement: API designed for 99.9% uptime (Phase 2 with distributed tracing)
- Acceptance Criteria:
  - Stateless API servers (no session affinity required)
  - Database connection pooling and retry logic
  - Distributed cache (Redis) for session/token data
  - Health checks endpoint (`/health/ready`)
  - Graceful shutdown (drain in-flight requests)
- Implementation Status: **PHASE 2** (Stateless design in place, distributed cache via ICacheService)
- Evidence: Redis configuration, health checks in Program.cs

### NFR-006: Scalability
- Requirement: Support 10x user growth without code changes
- Acceptance Criteria:
  - Horizontal scaling: multiple API instances behind load balancer
  - Async messaging via MassTransit (decouples modules)
  - Background jobs via Hangfire (queued, not blocking)
  - Read/write separation (Dapper reads scale independently)
  - Caching reduces database load
- Implementation Status: **PHASE 2** (Architecture supports it; extraction to microservices in Phase 2)
- Evidence: MassTransit configuration for async events, Hangfire background jobs, read-side Dapper queries

### NFR-007: Testability
- Requirement: >80% code coverage with automated tests
- Acceptance Criteria:
  - Unit tests for business logic (NSubstitute mocks, no database)
  - Integration tests for API contracts (real database via Testcontainers)
  - Architecture tests enforce layer separation (NetArchTest.Rules)
  - Test fixtures reusable (WebAppFixture, SqlServerFixture)
  - CI/CD pipeline runs all tests before merge
- Implementation Status: **COMPLETE** (224 tests passing, 80%+ coverage across 18 projects)
- Evidence: 104 unit tests (Identity 33, Audit 34, Notifications 21, Reporting 16), 18 contract tests (API envelopes, event schemas, error codes), 102 integration tests (module + legacy). TestAuthHandler + SqlServerFixture standardized. CI/CD validated. Latest: reporting module tests (16 tests).

---

## Technical Constraints

### TC-001: .NET 10 Requirement
- Constraint: All code must target .NET 10 (LTS until Nov 2027)
- Rationale: Stability, security updates, compatibility with government IT infrastructure
- Implementation: `net10.0` target framework in all .csproj files

### TC-002: SQL Server 2022 Database
- Constraint: SQL Server 2022 Enterprise is the only datastore on this repository. Oracle 19c port lives in [AqtCoreFW_Oracle](https://github.com/thiennd212/AqtCoreFW_Oracle) (post 2026-04-27 split). No PostgreSQL/MySQL on roadmap.
- Rationale: Always Encrypted support, government DBMS compliance, licensing parity with most VN GOV procurement.
- Implementation: EF Core SqlServer provider; no runtime DB-provider abstraction — multi-provider unification was evaluated 2026-04-27 and cancelled.

### TC-003: OpenIddict (On-Premise OIDC)
- Constraint: Authorization server must be self-hosted (no cloud auth providers)
- Rationale: Compliance with NĐ53 (data residency), NĐ59 (VNeID integration)
- Implementation: `AqtCoreFW.AuthServer` project, OpenIddict 7.x, ASP.NET Identity

### TC-004: Vietnamese Language First
- Constraint: All user-facing messages and validation errors in Vietnamese (with English fallback)
- Rationale: Government users, compliance requirement
- Implementation: `WithMessage()` in FluentValidation includes Vietnamese text, API response `detail_vi` field

### TC-005: Module Isolation
- Constraint: Modules communicate **only** via SharedKernel interfaces or domain events (no direct project references)
- Rationale: Enables Phase 2 microservices extraction without code refactoring
- Implementation: Solution structure with separate project per layer per module, MassTransit event bus

### TC-006: Clean Architecture Enforcement
- Constraint: Dependency arrows point inward only (Presentation → Application → Domain ← Infrastructure)
- Rationale: Domain remains pure business logic, testable, independent of frameworks
- Implementation: NetArchTest.Rules in `AqtCoreFW.Architecture.Tests`, CI/CD gate on architecture violations

---

## Phase Breakdown & Status (2026-05-10)

| Phase | Name | Status | Completion | Test Count |
|-------|------|--------|------------|-----------|
| **F-Core** | Identity, SharedKernel, Infrastructure | **COMPLETE** | 100% | 33 unit |
| **F-Notifications** | Notifications, Audit, MasterData | **COMPLETE** | 100% | 55 unit |
| **F-AI** | AI module (LLM, Qdrant, data sovereignty) | **COMPLETE** | 100% | 16 unit |
| **F-Forms** | Forms (dynamic DDL, dual-mode storage, field types) | **COMPLETE** | 100% | 76 unit |
| **F-Workflow** | WorkflowEngine state machine + delegation + analytics + migration | **COMPLETE** | 100% | 260+ unit |
| **F-Files** | Files (MinIO, ClamAV, versioning, lifecycle) | **COMPLETE** | 100% | 19 unit |
| **F-Integration** | Integration (YARP, webhooks, API keys, outbox) | **COMPLETE** | 100% | 75 unit |
| **F-Organization** | Organization hierarchy + RLS enforcement | **COMPLETE** | 100% | 143 unit |
| **F-SystemParams** | System-wide configuration parameters | **COMPLETE** | 100% | — |
| **F-Reporting** | Reporting (KPI dashboard, exports, query catalog) | **COMPLETE** | 100% | 29 unit |
| **F-Rules** | Rules engine (decision tables, NCalc eval) | **COMPLETE** | 100% | 49 unit |
| **F-Collaboration** | Chat, messaging, threading (SignalR) | **COMPLETE** | 100% | 60 unit |
| **QĐ 742 Phase-01** | Audit Log Baseline (5 categories, immutability, retention) | **COMPLETE** | 100% | 22+15+187 unit |
| **QĐ 742 Phase-02** | Auth Lifecycle (1st-login pwd, lockout escalation, MFA requirement) | **COMPLETE** | 100% | 305+20+19 unit |
| **QĐ 742 Phase-03** | MFA Policy UI (TOTP, recovery codes, enrolment wizard) | **COMPLETE** | 100% | 66 integration |
| **QĐ 742 Phase-04** | Data Classification (PII encryption, EmailHash split, RLS guards) | **COMPLETE** | 100% | 66+89 integration |
| **QĐ 742 Phase-06** | Vault Key Rotation (IKeyResolver, v{N}: prefix, key versioning) | **COMPLETE** | 100% | 9 architecture |
| **P1: Production** | CI/CD, Vault, Docker hardening, k6 tests | **COMPLETE** | 100% | 18 contract |
| **P2: Operations** | Helm production-grade, Prometheus, Redis, Serilog | **COMPLETE** | 100% | 102 integration |
| **V1.5: Gap Closure** | Feature flags, contracts, AsyncAPI, performance | **COMPLETE** | 100% | — |
| **P3: Phase 2 Plan** | Microservices extraction + UI template | Planned | 0% | — |

---

## Success Metrics (All Core Modules Complete - 2026-03-18)

| Metric | Target | Current | Status |
|--------|--------|---------|--------|
| Backend Modules | 13 complete | **13/13 COMPLETE** (Identity, Notifications, Audit, Forms, AI, Cases, Files, Integration, Organization, SystemParams, Workflow, MasterData, Reporting) | ✓ Complete |
| Test Suite | >80% coverage | 224 tests: 104 unit + 18 contract + 102 integration | ✓ Complete |
| Unit Test Count | Per module | Identity 33, Audit 34, Notifications 21, Reporting 16 | ✓ Complete |
| Contract Tests | API validation | 18 contract tests (envelopes, events, errors) | ✓ Complete |
| Architecture Compliance | 100% (NetArchTest) | All layers validated (Domain, Application, Infrastructure, Presentation) | ✓ Complete |
| Test Execution Time | <5 minutes | ~2 minutes (local), ~6 min (CI) | ✓ Complete |
| k6 Load Testing | All modules | 131K cases/15min, 0.03% error rate, Tier 500 baseline | ✓ Complete |
| CI/CD Pipeline | GitHub Actions + SAST/SCA | Build → Test → Docker → Registry → SBOM | ✓ Complete |
| Security Audit | 35 findings | 31/35 remediated (88.6%), Groups A-D complete | ✓ Complete |
| Production Readiness | P1 gate passed | Vault, Docker hardened, Helm charts ready | ✓ Complete |
| Operational Excellence | P2 monitoring | Prometheus + Grafana + Serilog, Redis distributed cache | ✓ Complete |
| Compliance Coverage | 90%+ standards | Law 91, NĐ53, NĐ59 (stub), NĐ68 (stub), TT12 (partial) | ✓ On Track |

---

## Dependencies & External Integrations

| Dependency | Version | Purpose | Status |
|---|---|---|---|
| OpenIddict | 7.x | OIDC/OAuth2 server | Integrated |
| EF Core | 10.0 | ORM (writes) | Integrated |
| Dapper | 2.x | SQL query mapper (reads) | Integrated |
| MassTransit | 8.x | Event bus, async messaging | Integrated |
| Hangfire | 1.8.x | Background job scheduler | Integrated |
| Redis | 7.x | Distributed cache | Integrated (Docker) |
| SQL Server | 2022 | Primary database | Integrated (Docker) |
| MinIO | Latest | Object storage | Planned (Phase 03) |
| ClamAV | Latest | Virus scanning | Planned (Phase 03) |
| HashiCorp Vault | 1.x | Secrets management | Integrated (Docker) |
| RabbitMQ | 4.x | Message broker | Integrated (Docker) |
| OpenTelemetry | 1.15 | Distributed tracing | Integrated |
| Serilog | 4.3 | Structured logging | Integrated |
| Testcontainers | Latest | Integration test DB isolation | Integrated |

---

## Risk Assessment

| Risk | Severity | Mitigation | Owner |
|---|---|---|---|
| VNeID integration not ready on time | HIGH | Start integration early (Q4 2025), maintain stub for testing | Lead |
| Performance degradation under load | MEDIUM | Load testing Phase 06, optimize queries early | Tech Lead |
| Security compliance gap | HIGH | Monthly compliance audit, pentest Q2 2026 | Security Lead |
| Database migration complexity | MEDIUM | Testcontainers for safe testing, rollback procedures documented | DBA |
| Module interdependency violation | LOW | Architecture tests block builds, code review gates | Tech Lead |

---

## Development Roadmap

### Q4 2025 (Current)
- [x] Phase 01: Foundation complete (Identity, SharedKernel)
- [x] Phase 10: Testing infrastructure complete
- [ ] Phase 02: Cases module 50% (DDD example)
- [ ] Phase 08: Docker Compose dev setup complete

### Q1 2026
- [ ] Phase 02: Cases module complete (CQRS, state machine)
- [x] All 13 backend modules — COMPLETE (2026-03-18)
- [x] 224 tests passing (unit + contract + integration)
- [x] CI/CD, Docker, Helm, k6 — production-ready

### Q2 2026
- [ ] Frontend Core Base V1 (React + TypeScript + Ant Design)
- [ ] Security audit Groups E-F remediation
- [ ] Pilot deployment on target GOV project

### Q3 2026
- [ ] Frontend V1.5 (design system, workflow/audit UI packs)
- [ ] Microservices extraction planning (if scale demands)
- [ ] Performance optimization post-launch
- [ ] Phase 2 architecture decision

---

## Sign-Off & Approvals

| Role | Name | Approval | Date |
|---|---|---|---|
| Project Manager | TBD | Pending | TBD |
| Tech Lead | TBD | Pending | TBD |
| Security Lead | TBD | Pending | TBD |
| Government Stakeholder | TBD | Pending | TBD |

---

## Appendix: Related Documents

- [`docs/system-architecture.md`](./system-architecture.md) — Architecture deep-dive
- [`docs/code-standards.md`](./code-standards.md) — Coding conventions
- [`docs/codebase-summary.md`](./codebase-summary.md) — Module inventory
- [`docs/tech-stack.md`](./tech-stack.md) — Technology decisions
- [`docs/deployment-guide.md`](./deployment-guide.md) — Ops procedures
- [`docs/compliance-evidence-checklist.md`](./compliance-evidence-checklist.md) — Audit checklist
