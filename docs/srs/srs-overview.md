---
title: SRS - Software Requirements Specification (Overview)
standard: IEEE 830-1998
snapshot_commit: a593d719
snapshot_version: v2.137p113
date: 2026-05-25
language: vi (tech terms EN)
---

# SRS — Software Requirements Specification (Overview)

> IEEE 830-1998 Software Requirements Specification template cho **AqtCoreFW** — Production-ready .NET 10 backend framework cho Vietnamese Government projects.

---

## Table of Contents

1. [Introduction](#1-introduction)
   - [1.1 Purpose](#11-purpose)
   - [1.2 Scope](#12-scope)
   - [1.3 Definitions, Acronyms, Abbreviations](#13-definitions-acronyms-abbreviations)
   - [1.4 References](#14-references)
   - [1.5 Overview](#15-overview)
2. [Overall Description](#2-overall-description)
   - [2.1 Product Perspective](#21-product-perspective)
   - [2.2 Product Functions](#22-product-functions)
   - [2.3 User Classes & Characteristics](#23-user-classes--characteristics)
   - [2.4 Operating Environment](#24-operating-environment)
   - [2.5 Design & Implementation Constraints](#25-design--implementation-constraints)
   - [2.6 Assumptions & Dependencies](#26-assumptions--dependencies)
3. [Specific Requirements](#3-specific-requirements)
   - [3.1 Functional Requirements](#31-functional-requirements)
   - [3.2 Non-Functional Requirements](#32-non-functional-requirements)
   - [3.3 External Interface Requirements](#33-external-interface-requirements)

---

## 1. Introduction

### 1.1 Purpose

Tài liệu SRS này định nghĩa toàn bộ yêu cầu chức năng, phi chức năng, và giao diện của **AqtCoreFW** — một framework backend hoàn chỉnh dành cho các dự án chính phủ Việt Nam.

**Đối tượng sử dụng:**
- **Nhà quản lý dự án:** Xác định phạm vi, tài nguyên, thời gian biểu
- **Nhà phân tích kinh doanh:** Ánh xạ yêu cầu nghiệp vụ sang UC chi tiết
- **Nhà phát triển:** Hiểu ranh giới hệ thống, ràng buộc kỹ thuật, trách nhiệm từng module
- **Kiểm thử viên:** Thiết kế test case dựa trên yêu cầu chức năng + phi chức năng
- **Kiểm toán/Tuân thủ:** Đánh giá khả năng đáp ứng luật pháp (Law 91/2025, NĐ53/59/68/85)

**Mục tiêu:** Cung cấp "khung chuẩn" (reference frame) giúp tất cả bên liên quan nắm bắt:
- Phạm vi sản phẩm (in / out of scope)
- Kiến trúc, công nghệ, ràng buộc
- Đối tượng người dùng (user personas)
- Các yêu cầu cấp cao (được chi tiết hóa trong use-case phases)

### 1.2 Scope

**Sản phẩm:** AqtCoreFW v2.137 — Modular Monolith backend + React 19 SPA + OpenIddict Auth Server.

**Đối tượng sản phẩm:**
- **Hệ thống quản lý luồng xử lý** (Workflow Management System) với hỗ trợ đa tầng tổ chức, đa phân quyền (RBAC/ABAC/SoD)
- **Hệ thống quản lý danh tính** (Identity & Access Management) tuân thủ NĐ59 (VNeID), NĐ85 (ATTT), NĐ53 (Cybersecurity)
- **Nền tảng hỗ trợ** bao gồm: quản lý biểu mẫu, files/storage, thông báo, kiểm toán, báo cáo, tích hợp bên thứ 3

**16 modules hiện tại:**
- **Core:** Identity, Workflow, Forms, BusinessProcess, Audit
- **Support:** Files, Notifications, Collaboration, Organization, MasterData
- **Extension:** Integration, Reporting, Rules, SystemParams, AI (reserved)

**Tổng 71 controllers, 356 MediatR handlers, 67 domain events, 17 business roles + 2 system roles.**

**Ngoài phạm vi (Excluded):**
- Public marketplace / Citizen portal (Phase 2+)
- Mobile app (iOS/Android) — Web/SPA only Phase 1
- ML-based scoring / Advanced analytics (deferred use-cases)
- Real-time chat (Collaboration module stub only)

**Tuân thủ pháp lý:**
- Law 91/2025/QH15 (PDPL — Privacy/Personal Data Protection)
- Decree 356/2025 (replaces NĐ13) — Personal Data Processing
- NĐ53/2023 (Cybersecurity baseline)
- NĐ59/2023 (VNeID federation)
- NĐ68/2023 (Digital Signatures)
- NĐ85/TT12 (Information Security — ATTT)
- QĐ742 (Government IT Standard — QCVN compliance)

### 1.3 Definitions, Acronyms, Abbreviations

| Term | Định nghĩa | Notes |
|---|---|---|
| **Tenant** | Tổ chức quản lý; instance dữ liệu riêng biệt | Soft multi-tenancy via `TenantId` column |
| **RBAC** | Role-Based Access Control — Phân quyền dựa vai trò | Foundation; 17 BTC roles + 2 system roles seeded |
| **ABAC** | Attribute-Based Access Control — Phân quyền dựa thuộc tính | Fine-grained; supports dept, classification, team-based rules |
| **SoD** | Separation of Duties — Tách rời chức năng | Conflict rules prevent user holding incompatible roles simultaneously |
| **Workflow Instance** | Một case/hồ sơ đi qua các trạng thái, gán cho người dùng/nhóm xử lý | State machine + task pool model |
| **Workflow Task** | Công việc gán cho một người trong một state | Claimable (pool) hoặc pre-assigned |
| **Candidate Index** | Read-side projection để tìm tasks eligible cho người dùng | ByPermission, ByRole, ByGroup, ByOrgUnit pooling strategies |
| **Outbox Pattern** | Reliable async events; write event → publish table → consumer pulls | Ensures exactly-once semantics for inter-module communication |
| **MFA** | Multi-Factor Authentication — Xác thực đa yếu tố | TOTP-based (Google/Microsoft Authenticator); backup codes |
| **VNeID** | Vietnam e-ID federation per NĐ59 | eKYC levels 1-3 (ID card → FA → video) |
| **RTBF** | Right-to-be-Forgotten — Quyền bị quên | GDPR/PDPL Article 17; anonymization, data purge on request |
| **JWT** | JSON Web Token — Bearer token for API auth | OpenIddict-issued; includes `sub`, `tenant_id`, `roles`, `dept_code` claims |
| **OIDC** | OpenID Connect — Federation protocol | Authorization Code flow; on-prem AuthServer (OpenIddict 7.x) |
| **FSM** | Finite State Machine — Máy trạng thái | Workflow definition (states + transitions); workflow instance (current state) |
| **CQRS** | Command Query Responsibility Segregation | Write via EF Core + MediatR commands; read via Dapper queries |
| **DDD** | Domain-Driven Design — Thiết kế driven theo domain | Bounded contexts; AggregateRoot, Value Objects, Domain Events |
| **RowVersion** | SQL Server optimistic concurrency token | byte[8] mapping to ETag header; 409 Conflict on mismatch |
| **ETag** | Entity Tag — Conditional HTTP semantics | If-Match / If-None-Match for optimistic updates |
| **OrgUnit** | Organizational Unit — Đơn vị tổ chức | Hierarchy (parent-child); staff assigned to multiple OrgUnits |
| **CAN_** | Permission code prefix for capability checks | e.g., `CAN_APPROVE_TASK`, `CAN_VIEW_AUDIT_LOG` |
| **Policy** | ASP.NET Core Authorization Policy | Declarative `[Authorize(Policy = "...")]` attribute |
| **IModuleClient** | Cross-module async interface; published via SharedKernel | Dapper-based read-side calls; ensures no circular dependencies |
| **Always Encrypted** | SQL Server encryption feature for PII columns | Customer master key (CMK) in Vault; transparent to application |
| **Dapper** | Micro-ORM for read-side queries | Direct SQL; no state tracking; faster than EF for reporting |
| **DbContext** | EF Core per-module database context | Write-side only; global query filters for tenant isolation |
| **Handler** | MediatR request handler (Command/Query) | Implements IRequestHandler<TRequest, TResponse> |
| **Domain Event** | Immutable event raised by aggregate | Published to Outbox → consumed by MassTransit subscribers |
| **Consumer** | MassTransit endpoint subscribing to domain events | Async event handler; transactional guarantee via Outbox |
| **Hangfire** | Background job scheduler | Cron + manual enqueue; persisted in SQL Server [hangfire] schema |
| **MinIO** | S3-compatible object storage | File uploads; local Docker dev, S3 prod; ClamAV virus scan on ingest |
| **Redis** | In-memory cache | IMemoryCache (dev) / Redis (prod) via ICacheService |
| **Vault** | HashiCorp Vault for secrets | Connection strings, API keys, CMK, JWT signing key |
| **NĐ** | Nghị định (Government Decree) | e.g., NĐ53, NĐ59, NĐ68, NĐ85 |
| **QĐ** | Quyết định (Government Decision) | e.g., QĐ742 (IT Standard) |
| **BTC** | Bộ Tư lệnh (ministry seed role hierarchy) | 17 business roles from MINISTER down to STAFF |
| **PII** | Personally Identifiable Information | Name, ID number, phone, email, address — encrypted at rest |
| **Compliance** | Meeting regulatory requirements | Law 91, NĐ53/59/68/85, OWASP, STRIDE |
| **API Gateway** | YARP reverse proxy | Route to auth server / api / frontend; rate limiting, auth |
| **Orval** | OpenAPI SDK generator | Auto-gen TypeScript React hooks from OpenAPI 3.1 specs |

### 1.4 References

**Internal Documentation:**
- [`docs/project-overview-pdr.md`](../project-overview-pdr.md) — Product Definition Reference (FR/NFR, status)
- [`docs/system-architecture.md`](../system-architecture.md) — Tech stack, layer model, module map
- [`docs/codebase-summary.md`](../codebase-summary.md) — File structure, coding patterns per module
- [`docs/dependency-map.md`](../dependency-map.md) — Module relationships, entity maps, FE↔BE contracts
- [`docs/code-standards.md`](../code-standards.md) — Naming, patterns, testing conventions
- [`docs/tech-stack.md`](../tech-stack.md) — Runtime, frameworks, versions
- [`docs/compliance-evidence-checklist.md`](../compliance-evidence-checklist.md) — Law 91/NĐ53/NĐ59/NĐ68/NĐ85 mapping
- [`docs/api-reference-vi.md`](../api-reference-vi.md) — OpenAPI 3.1 reference (auto-generated)
- [`README.md`](../../README.md) — Quick start, architecture overview

**Standards:**
- IEEE 830-1998 — Software Requirements Specification
- Alistair Cockburn, Writing Effective Use Cases
- OWASP Top 10 2023
- Secure Coding Standards (OWASP, CWE)

**External Standards (Regulatory):**
- Law 91/2025/QH15 (PDPL)
- Decree 356/2025 (Data Processing)
- NĐ53/2023 (Cybersecurity)
- NĐ59/2023 (VNeID)
- NĐ68/2023 (Digital Signatures)
- NĐ85/TT12 (Information Security)
- QĐ742 (Government IT)

### 1.5 Overview

**Tài liệu SRS này** được tổ chức thành **8 phase:**

1. **Phase 01 — Scout & Inventory** (DONE) — Scan codebase → extract 16 modules, 71 controllers, 356 handlers, roles, permissions
2. **Phase 02 — SRS Core (Overview)** (THIS FILE) — IEEE 830 §1-3 (Introduction, Overall Description, high-level requirements)
3. **Phase 03 — Use Cases: Identity** — UC-IDN-01 through UC-IDN-15 (auth, RBAC, ABAC, delegation, etc.)
4. **Phase 04 — Use Cases: Workflow** — UC-WF-01 through UC-WF-12 (definition, instance, task, assignment)
5. **Phase 05 — Use Cases: Forms & Business Process** — UC-FORM-01..10, UC-BP-01..05
6. **Phase 06 — Use Cases: Supporting Modules** — UC-FILES, UC-NOTIF, UC-AUDIT, UC-INTEG, UC-COLLAB, UC-ORG
7. **Phase 07 — Use Cases: Platform** — UC-REPORT, UC-RULES, UC-MASTERDATA, UC-SYSPARAMS, UC-AI
8. **Phase 08 — SRS NFR + Interfaces + Index** — IEEE 830 §3.2-3.3 detailed (performance, security, interface specs)

**Mỗi Use Case (UC)** theo Cockburn form: **Goal**, **Primary Actor**, **Scope**, **Level**, **Stakeholders & Interests**, **Success Scenario**, **Extensions**.

**Mỗi Use Case** tham chiếu từ Functional Requirement (FR-*-NN) ở phần **3.1** dưới đây. Ví dụ: FR-IDN-01 (User Authentication) được chi tiết hóa trong UC-IDN-01, UC-IDN-02.

---

## 2. Overall Description

### 2.1 Product Perspective

AqtCoreFW là một **modular monolith** — 16 modules tích hợp với nhau trong cùng một process, trên nền tảng `.NET 10 + ASP.NET Core`. Không có external OIDC server (VD OpenID Connect không phải Keycloak); authentication/authorization tích hợp sẵn via **OpenIddict 7.x** embedded.

**Kiến trúc tầng (Layer):**

```
┌─────────────────────────────────┐
│ Presentation (Controllers, API) │  71 controllers, 160+ routes/module
├─────────────────────────────────┤
│ Application (MediatR handlers)  │  356 handlers, CQRS pipeline
├─────────────────────────────────┤
│ Domain (Entities, Events)       │  DDD bounded contexts
├─────────────────────────────────┤
│ Infrastructure (EF, Dapper)     │  SQL Server writes + Dapper reads
├─────────────────────────────────┤
│ SharedKernel (Interfaces, base) │  Cross-module contracts
└─────────────────────────────────┘
```

**Externals:**
- **SQL Server 2022** — Write-side (EF Core) + read-side (Dapper)
- **Redis** — Cache (dev: IMemoryCache; prod: Redis)
- **MinIO** — S3-compatible storage (files, documents)
- **RabbitMQ** — Async events (MassTransit)
- **Hangfire** — Background jobs
- **HashiCorp Vault** — Secrets, encryption keys
- **OpenTelemetry** — Observability (traces, metrics, logs)

**Integration points:**
- **Frontend:** React 19 SPA (separate `web/` folder) → calls API via OpenAPI-gen SDK (Orval)
- **VNeID:** stub connector (replace with real endpoint per NĐ59)
- **Digital Signature:** stub (replace with real CA provider per NĐ68)
- **Webhook:** outbound integration for 3rd-party systems

Chi tiết → [`docs/system-architecture.md`](../system-architecture.md).

### 2.2 Product Functions

**16 modules, mỗi module có 1 set chức năng chính:**

| Module | Chức năng Chính | Endpoints | Handlers | Notes |
|--------|---|---|---|---|
| **Identity** | Đăng nhập, RBAC, ABAC, MFA, VNeID, delegation, access review, digital signature | 160 | 45 | Core security; 21 controllers; 17 BTC roles + 2 system roles |
| **Workflow** | Định nghĩa luồng xử lý FSM, tạo instance, gán task (ByPermission/ByRole/Group/OrgUnit), xử lý, phân tích | 48 | 38 | 7 controllers; task pool + assignment strategies; inbox queries |
| **Forms** | Thiết kế biểu mẫu (no-code), submit, phê duyệt, public form, custom views | 46 | 22 | 6 controllers; field validation, conditional logic, versioning |
| **BusinessProcess** | Định nghĩa quy trình kinh doanh (multiple forms ↔ states), chạy instance, form mapping | 10 | 8 | 1 controller; orchestrate forms + workflow + rules |
| **Audit** | Log mọi hành động, query, export CSV, RTBF request, retention policy, security incident tracking | 35 | 20 | 6 controllers; append-only HMAC chain; compliance per NĐ53 |
| **Files** | Upload/download files, version history, virus scan (ClamAV), digital signature, retention | 30 | 15 | 4 controllers; MinIO storage; encryption at rest |
| **Notifications** | Gửi email (MailKit), SMS (webhook), in-app (SignalR), template Liquid | 16 | 12 | 2 controllers; queue-based delivery |
| **Organization** | Quản lý đơn vị tổ chức (OrgUnit hierarchy), staff assignment | 18 | 10 | 2 controllers; M:N staff-OrgUnit; org unit code key |
| **Collaboration** | Conversation, message, thread (real-time via SignalR) | 14 | 8 | 2 controllers; in-app messaging |
| **MasterData** | Reference data (tỉnh/huyện/xã), lookup caching, dictionary, workflow taxonomy | 36 | 18 | 5 controllers; seeded; cached per tenant |
| **SystemParams** | Global system configuration (email host, feature flags, timeouts) | 8 | 6 | 1 controller; audit trail on change |
| **Integration** | Partner system CRUD, contract (endpoint) management, message log, API key M2M | 22 | 14 | 3 controllers; webhook engine for outbound |
| **Reporting** | Report builder, dashboards, widgets, data export (Excel/PDF) | 24 | 16 | 3 controllers; scheduled report generation |
| **Rules** | Rule set builder (declarative), decision tables, rule evaluation (runtime) | 24 | 14 | 3 controllers; input/output mapping |
| **AI** | (Reserved) ML scoring, entity extraction, recommendations | — | — | Placeholder for future phases |

**Total:** 71 controllers, 356 handlers, 67 domain events, 48+ API routes/module average.

### 2.3 User Classes & Characteristics

**5 Business Personas + 3 Special Roles:**

| Persona | Vietnamese Name | Typical Workflow | Role(s) | Concerns | Module Usage |
|---|---|---|---|---|---|
| **Minister/Apex** | Bộ trưởng / Lãnh đạo cấp 1 | Review summary, approve policy, delegate decisions | BTC_MINISTER | Accountability, audit trail, delegation limits | Audit, Reporting, Workflow (apex approval) |
| **Manager** | Cấp quản lý (Vụ trưởng / Phó Cục) | Create workflow template, assign team tasks, review submissions, export reports | BTC_DEPT_DIRECTOR, BTC_DEPT_DEPUTY | Visibility into team workload, permission/role mgmt | Workflow (define/assign), Forms (template), Reporting, Identity (user mgmt) |
| **Specialist** | Chuyên viên / Công chức | Submit form, claim & process workflow task, upload file, document actions | BTC_SPECIALIST, BTC_OFFICER, BTC_STAFF | Task inbox clarity, form validation, file upload limits | Workflow (task claim), Forms (submit), Files (upload), Notifications |
| **Inspector/Auditor** | Thanh tra viên / Kiểm toán viên | Query audit log, export compliance report, access review, verify digital signature | BTC_INSPECTOR, BTC_AUDITOR | Complete audit trail, tamper-proof logs, exportable proof | Audit (query/export), Identity (access review), Files (signature verify) |
| **Admin (Tenant)** | Quản trị viên hệ thống | User CRUD, role assignment, permission sync, system parameter tuning, backup scheduling | TENANT_ADMIN | Security (SoD, MFA enforcement), compliance (RTBF handling), uptime | Identity (all admin endpoints), SystemParams, Audit |
| **System** | (Hệ thống / Internal) | Scheduled job (Hangfire), event consumer (MassTransit), background processing | (no UI role) | Reliability, exactly-once semantics for events | Hangfire jobs, MassTransit consumers (internal) |
| **Integration** | (Bên thứ 3 / Partner) | Call API via M2M key, receive webhook callback | (API key, no user login) | API stability, webhook retry, rate limiting | Integration (partner contracts), Notifications (outbound webhook) |

**Thay đổi theo ngữ cảnh:**
- **Trong quá trình xử lý hồ sơ:** Công chức claim task → phê duyệt → escalate bến manager
- **Kiểm toán:** Inspector query audit → export → file locally
- **User onboarding:** Admin tạo user → VNeID eKYC → assign OrgUnit + role → user đăng nhập lần đầu (MFA setup)

Chi tiết role hierarchy + permission seeding → [`plans/260525-2149-srs-and-usecases/research/inventory-actors-and-permissions.md`](../../plans/260525-2149-srs-and-usecases/research/inventory-actors-and-permissions.md) section 3.

### 2.4 Operating Environment

**Backend Runtime:**
- **.NET 10 Runtime** (LTS, 3-year support)
- **ASP.NET Core 10** — HTTP, middleware, dependency injection
- **OS:** Linux (production: Red Hat/Ubuntu) / Windows (dev)
- **Container:** Docker, orchestration (Kubernetes Phase 2)

**Database:**
- **SQL Server 2022** — Writes (EF Core) + reads (Dapper)
- **Connection:** TCP 1433, Windows Auth (dev) / SQL Auth (prod via Vault)
- **Features:** Always Encrypted (PII), computed columns (audit), full-text search (FTS)

**Cache & Async:**
- **Cache:** IMemoryCache (dev) / Redis (prod)
- **Job Queue:** Hangfire (scheduled jobs, manual enqueue)
- **Async Events:** RabbitMQ + MassTransit (inter-module, Outbox pattern)

**Storage:**
- **MinIO** (S3-compatible object storage) — files, documents, attachments
- **Local:** Docker `miniodata` volume (dev)

**Observability:**
- **Logs:** Serilog → Console/File (dev) / Elasticsearch (prod)
- **Metrics:** Prometheus + Grafana
- **Traces:** OpenTelemetry → Jaeger/Datadog

**Security:**
- **Auth Server:** OpenIddict 7.x (separate `AqtCoreFW.AuthServer` host) — OIDC/OAuth2
- **Secrets:** HashiCorp Vault (dev local, prod HA cluster)
- **TLS:** 1.2+ required; mutual TLS for M2M (phase 2)

**Network:**
- **API Gateway:** YARP (reverse proxy) — route to auth server, API, frontend
- **DNS:** Internal (dev) / Public + Split-DNS (prod)
- **Firewall:** Ingress 443 (API), 8086 (auth), 3000 (frontend)

### 2.5 Design & Implementation Constraints

**Architectural Constraints:**
1. **Modular Monolith Pattern** — Single .NET process; no microservices until Phase 2 (strangler)
2. **Clean/Onion Architecture** — Domain → Application → Infrastructure → Presentation (dependency inward only)
3. **DDD-lite** — Bounded contexts, aggregates, domain events (not full tactical DDD)
4. **CQRS** — Write via EF Core + MediatR commands; read via Dapper queries
5. **Soft Multi-tenancy** — Single database; `TenantId` column + global query filter per context
6. **No circular module dependencies** — enforced via SharedKernel.Authorization contracts

**Technology Constraints:**
1. **EF Core 10 for writes only** — Change tracking, migrations, relationships
2. **Dapper for read-side queries** — Direct SQL; no ORM overhead for reporting/analytics
3. **MassTransit + Outbox** — exactly-once event delivery; no distributed transactions
4. **SQL Server 2022 only** (Phase 1) — Oracle lives in separate `AqtCoreFW_Oracle` repo
5. **No ORM for read-side** — Dapper must be used for queries; Entity Framework forbidden in queries
6. **One DbContext per module** — Schema isolation; no cross-module EF navigation; IModuleClient (Dapper-backed) for reads

**Compliance Constraints:**
1. **PII Encryption** — Law 91/Decree 356: Name, phone, email, address fields encrypted via Always Encrypted
2. **Audit Chain** — NĐ53: Every action logged immutably with HMAC chaining (append-only audit table)
3. **RTBF (Right-to-be-Forgotten)** — PDPL Article 17: Anonymization endpoint; scheduled purge
4. **Data Retention** — Configurable per entity type; scheduled deletion job
5. **Digital Signature** — NĐ68: stub integration (replace with VN CA provider)
6. **VNeID Federation** — NĐ59: stub (replace with real VNeID OAuth endpoint)
7. **Role Separation (SoD)** — NĐ85/TT12: conflict rules prevent user holding incompatible roles

**Code Quality Constraints:**
1. **Unit Test Coverage** — ≥80% for business logic
2. **Integration Test** — ≥50 tests per module
3. **No hardcoded secrets** — All via HashiCorp Vault or appsettings (dev only)
4. **Linting** — StyleCop, FxCopAnalyzers enabled; 0 compiler warnings
5. **File size** — Max 200 lines per code file (modularization requirement per CLAUDE.md)

### 2.6 Assumptions & Dependencies

**Assumptions:**
1. SQL Server 2022 instance available & initialized (init scripts run)
2. Redis available for caching (dev: localhost:6379, prod: managed service)
3. MinIO available for file storage (dev: Docker, prod: S3-compatible)
4. RabbitMQ available for event bus (dev: Docker, prod: managed)
5. Vault unsealed & accessible (dev: dev-mode, prod: HA cluster)
6. Network connectivity to external services (VNeID, digital sig provider, email SMTP)
7. Time synchronization (NTP) for TOTP/JWT expiry
8. VNeID OAuth endpoint & Digital Sig provider reachable (stubs during Phase 1)

**External Dependencies:**
- **VNeID Provider** — OAuth2 endpoint per NĐ59 (eKYC levels 1-3)
- **Digital Signature Provider** — VN CA (VNCA) for certificate-based signing per NĐ68
- **Email SMTP** — Outbound email delivery (MailKit)
- **SMS Provider** — Webhook-based SMS (Twilio, Viettel, etc. per tenant config)
- **Webhook Consumers** — 3rd-party systems listening for callbacks (Partner.ContractEndpoint)

**Service Level Agreements (SLA):**
- **API response time** — 95th percentile <500ms (see NFR details in Phase 08)
- **Database uptime** — 99.9% (backup, replication, failover)
- **Cache miss tolerance** — Read-through fallback to database
- **Event delivery** — exactly-once via Outbox; max 24h retry

---

## 3. Specific Requirements

### 3.1 Functional Requirements

**Functional Requirements (FR) at module level — chi tiết hóa trong use-case phases:**

| FR ID | Module | Requirement | Priority | Linked UC |
|---|---|---|---|---|
| **FR-IDN-01** | Identity | Hệ thống PHẢI cho phép user đăng nhập bằng tài khoản (username/password) | P0 | UC-IDN-01 |
| **FR-IDN-02** | Identity | Hệ thống PHẢI hỗ trợ OIDC/OAuth2 (implicit flow for SPA, authorization code for M2M) | P0 | UC-IDN-02 |
| **FR-IDN-03** | Identity | Hệ thống PHẢI cấp JWT access token + refresh token; token PHẢI chứa claims: sub, tenant_id, roles, dept_code | P0 | UC-IDN-01 |
| **FR-IDN-04** | Identity | Hệ thống PHẢI hỗ trợ TOTP MFA; admin CÓ THỂ buộc MFA cho user/role | P1 | UC-IDN-03 |
| **FR-IDN-05** | Identity | Hệ thống PHẢI hỗ trợ RBAC; user = multiple roles; role = permission set | P0 | UC-IDN-05 |
| **FR-IDN-06** | Identity | Hệ thống PHẢI hỗ trợ ABAC; deny access if user.dept ≠ resource.dept và classification level insufficient | P0 | UC-IDN-06 |
| **FR-IDN-07** | Identity | Hệ thống PHẢI enforce SoD rules; user KHÔNG THỂ hold incompatible role pairs | P1 | UC-IDN-07 |
| **FR-IDN-08** | Identity | Hệ thống PHẢI cho phép user tạo delegation (temporary role assumption) với effective date range | P2 | UC-IDN-08 |
| **FR-IDN-09** | Identity | Hệ thống PHẢI track access review; auditor PHẢI approve/reject user rights | P2 | UC-IDN-09 |
| **FR-IDN-10** | Identity | Hệ thống PHẢI hỗ trợ VNeID eKYC (stub); link external identity; validate CMND/Passport | P1 | UC-IDN-10 |
| **FR-IDN-11** | Identity | Hệ thống PHẢI hỗ trợ digital signature (stub); sign PDF/Word document; verify signature | P2 | UC-IDN-11 |
| **FR-IDN-12** | Identity | Hệ thống PHẢI quản lý user consent per Law 91 Article 11 (PDPL) | P1 | UC-IDN-12 |
| **FR-IDN-13** | Identity | Hệ thống PHẢI cung cấp admin endpoint để sync role↔permission assignments | P1 | UC-IDN-13 |
| **FR-IDN-14** | Identity | Hệ thống PHẢI track session (login/logout/timeout); admin PHẢI terminate session | P1 | UC-IDN-14 |
| **FR-IDN-15** | Identity | Hệ thống PHẢI validate credential policy (password strength, expiry, history) | P1 | UC-IDN-15 |
| **FR-WF-01** | Workflow | Hệ thống PHẢI cho phép định nghĩa workflow (states, transitions, assignment rules) | P0 | UC-WF-01 |
| **FR-WF-02** | Workflow | Hệ thống PHẢI cho phép tạo workflow instance từ form submission | P0 | UC-WF-01 |
| **FR-WF-03** | Workflow | Hệ thống PHẢI gán task cho user/nhóm dựa ByPermission / ByRole / ByGroup / ByOrgUnit strategy | P0 | UC-WF-02 |
| **FR-WF-04** | Workflow | Hệ thống PHẢI cho phép user claim task từ pool (self-assign) | P0 | UC-WF-03 |
| **FR-WF-05** | Workflow | Hệ thống PHẢI cho phép task transition (approve/reject) → next state | P0 | UC-WF-04 |
| **FR-WF-06** | Workflow | Hệ thống PHẢI cung cấp inbox query (personal tasks, deadline, priority) | P0 | UC-WF-02 |
| **FR-WF-07** | Workflow | Hệ thống PHẢI track task history (who claimed, when, duration) | P1 | UC-WF-05 |
| **FR-WF-08** | Workflow | Hệ thống PHẢI publish instance → 3rd-party webhook on state change | P2 | UC-WF-06 |
| **FR-WF-09** | Workflow | Hệ thống PHẢI hỗ trợ workflow migration (reindex candidate pool on role/group change) | P2 | UC-WF-07 |
| **FR-WF-10** | Workflow | Hệ thống PHẢI cung cấp analytics (task volume, SLA compliance, cycle time) | P2 | UC-WF-08 |
| **FR-FORM-01** | Forms | Hệ thống PHẢI cho phép tạo form template (drag-drop builder, field types) | P0 | UC-FORM-01 |
| **FR-FORM-02** | Forms | Hệ thống PHẢI validate form submission (required fields, type, conditional logic) | P0 | UC-FORM-02 |
| **FR-FORM-03** | Forms | Hệ thống PHẢI cho phép publish form → public/internal link | P1 | UC-FORM-03 |
| **FR-FORM-04** | Forms | Hệ thống PHẢI version form (draft/published); submitted instance PHẢI reference specific version | P1 | UC-FORM-04 |
| **FR-FORM-05** | Forms | Hệ thống PHẢI hỗ trợ conditional field display (show/hide field based on other field value) | P2 | UC-FORM-05 |
| **FR-FORM-06** | Forms | Hệ thống PHẢI hỗ trợ form submission approval (draft → submitted → approved) | P1 | UC-FORM-06 |
| **FR-FORM-07** | Forms | Hệ thống PHẢI support custom form view (filter, sort, export columns) | P2 | UC-FORM-07 |
| **FR-BP-01** | BusinessProcess | Hệ thống PHẢI cho phép tạo business process (multi-state, multi-form orchestration) | P1 | UC-BP-01 |
| **FR-BP-02** | BusinessProcess | Hệ thống PHẢI map form → state (form submission triggers state transition) | P1 | UC-BP-02 |
| **FR-BP-03** | BusinessProcess | Hệ thống PHẢI execute BP instance (run → collect forms → transition → complete) | P1 | UC-BP-03 |
| **FR-AUDIT-01** | Audit | Hệ thống PHẢI log mỗi hành động (user, action, resource, result, timestamp) | P0 | UC-AUDIT-01 |
| **FR-AUDIT-02** | Audit | Hệ thống PHẢI append-only logs với HMAC chaining (tamper detection) | P0 | UC-AUDIT-01 |
| **FR-AUDIT-03** | Audit | Hệ thống PHẢI export audit log (CSV, Excel) cho audit | P1 | UC-AUDIT-02 |
| **FR-AUDIT-04** | Audit | Hệ thống PHẢI track failed login attempts; block after threshold | P1 | UC-AUDIT-03 |
| **FR-AUDIT-05** | Audit | Hệ thống PHẢI accept RTBF request; anonymize user in audit log after grace period | P1 | UC-AUDIT-04 |
| **FR-FILE-01** | Files | Hệ thống PHẢI hỗ trợ file upload (single/multi), progress tracking | P0 | UC-FILE-01 |
| **FR-FILE-02** | Files | Hệ thống PHẢI scan file cho virus (ClamAV); quarantine if infected | P1 | UC-FILE-02 |
| **FR-FILE-03** | Files | Hệ thống PHẢI store file encryption at rest (Always Encrypted for PII) | P1 | UC-FILE-03 |
| **FR-FILE-04** | Files | Hệ thống PHẢI track file version history | P1 | UC-FILE-04 |
| **FR-FILE-05** | Files | Hệ thống PHẢI support document template (merge with form data) | P2 | UC-FILE-05 |
| **FR-NOTIF-01** | Notifications | Hệ thống PHẢI gửi in-app notification (inbox in UI) | P0 | UC-NOTIF-01 |
| **FR-NOTIF-02** | Notifications | Hệ thống PHẢI gửi email notification | P1 | UC-NOTIF-02 |
| **FR-NOTIF-03** | Notifications | Hệ thống PHẢI gửi SMS notification | P2 | UC-NOTIF-02 |
| **FR-NOTIF-04** | Notifications | Hệ thống PHẢI hỗ trợ notification template (Liquid syntax) | P1 | UC-NOTIF-03 |
| **FR-NOTIF-05** | Notifications | Hệ thống PHẢI track delivery status (sent/failed/bounced) | P2 | UC-NOTIF-04 |
| **FR-ORG-01** | Organization | Hệ thống PHẢI quản lý organization hierarchy (parent-child OrgUnit) | P0 | UC-ORG-01 |
| **FR-ORG-02** | Organization | Hệ thống PHẢI assign staff → OrgUnit (1 staff : N OrgUnits) | P0 | UC-ORG-02 |
| **FR-ORG-03** | Organization | Hệ thống PHẢI query org by code (unique per tenant) | P0 | UC-ORG-01 |
| **FR-INTEG-01** | Integration | Hệ thống PHẢI quản lý partner system (CRUD, endpoint contracts) | P1 | UC-INTEG-01 |
| **FR-INTEG-02** | Integration | Hệ thống PHẢI send webhook (async notification to partner) | P1 | UC-INTEG-02 |
| **FR-INTEG-03** | Integration | Hệ thống PHẢI retry failed webhook (exponential backoff) | P2 | UC-INTEG-03 |
| **FR-INTEG-04** | Integration | Hệ thống PHẢI track integration message log | P2 | UC-INTEG-04 |
| **FR-COLLAB-01** | Collaboration | Hệ thống PHẢI cho phép in-app message (user → user) | P2 | UC-COLLAB-01 |
| **FR-COLLAB-02** | Collaboration | Hệ thống PHẢI support conversation thread (async) | P2 | UC-COLLAB-02 |
| **FR-REPORT-01** | Reporting | Hệ thống PHẢI định nghĩa custom report (SQL template, filter, parameter) | P2 | UC-REPORT-01 |
| **FR-REPORT-02** | Reporting | Hệ thống PHẢI execute report → export (Excel, PDF, CSV) | P2 | UC-REPORT-02 |
| **FR-REPORT-03** | Reporting | Hệ thống PHẢI schedule report (Hangfire cron) | P2 | UC-REPORT-03 |
| **FR-RULES-01** | Rules | Hệ thống PHẢI tạo rule set (condition ↔ action mapping) | P2 | UC-RULES-01 |
| **FR-RULES-02** | Rules | Hệ thống PHẢI evaluate rule at runtime | P2 | UC-RULES-02 |
| **FR-RULES-03** | Rules | Hệ thống PHẢI support decision table (matrix rule evaluation) | P2 | UC-RULES-03 |

**Total:** 69 FR entries — high-level, mapped to use-case phases.

### 3.2 Non-Functional Requirements

**Chi tiết NFR (performance, security, usability, compatibility) được định nghĩa trong Phase 08 — `srs-nfr.md`.**

**High-level NFR categories:**
- **Performance** — Response time, throughput, capacity
- **Security** — Authentication, authorization, encryption, audit
- **Availability** — Uptime, disaster recovery, backup
- **Usability** — UI responsiveness, accessibility, i18n
- **Compliance** — Law 91, NĐ53/59/68/85, OWASP, PII protection
- **Maintainability** — Code quality, test coverage, documentation

Xem [`srs-nfr.md`](./srs-nfr.md) (Phase 08).

### 3.3 External Interface Requirements

**API, UI, Integration endpoints được định nghĩa trong Phase 08 — `srs-interfaces.md`.**

**Scope:**
- **HTTP REST API** — OpenAPI 3.1 spec (auto-generated từ controllers)
- **SignalR (Real-time)** — Notifications, collaboration, live updates
- **Webhook (Outbound)** — Integration endpoint contracts
- **Frontend UI** — React 19 SPA, Ant Design components

Xem [`srs-interfaces.md`](./srs-interfaces.md) (Phase 08).

---

## Conclusion

Tài liệu SRS này cung cấp **foundation** cho toàn bộ dự án. Các use-case phases (03-07) sẽ chi tiết hóa từng FR; phase 08 sẽ định nghĩa NFR + interface chi tiết.

**Next step:** Phase 03 — Use Cases: Identity (15 use cases).

---

**Document Version:** 2026-05-25 | **Snapshot:** v2.137p113 (commit `a593d719`)
