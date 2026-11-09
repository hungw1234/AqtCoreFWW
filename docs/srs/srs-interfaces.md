---
title: SRS — External Interface Requirements
standard: IEEE 830-1998 § 4
snapshot_commit: a593d719
snapshot_version: v2.137p113
date: 2026-05-25
language: vi (tech terms EN)
---

# SRS — External Interface Requirements

> IEEE 830 § 4 (External Interfaces) for **AqtCoreFW v2.137p113**

---

## Table of Contents

1. [§4.1 User Interfaces](#41-user-interfaces)
2. [§4.2 Hardware Interfaces](#42-hardware-interfaces)
3. [§4.3 Software Interfaces](#43-software-interfaces)
4. [§4.4 Communication Interfaces](#44-communication-interfaces)

---

## §4.1 User Interfaces

### 4.1.1 Frontend Application

**Platform:** React 19 Single-Page Application (SPA)  
**Build tool:** Vite (dev + prod optimization)  
**TypeScript:** v5.7 strict mode  
**UI Framework:** Ant Design (antd) v5.x with Vietnamese GOV color scheme  
**Component Library:** antd components + custom form builders

**Ref:** `docs/design-guidelines.md`, `docs/guides/orval-migration-guide.md`

### 4.1.2 Supported Browsers

| Browser | Min Version | Notes |
|---------|-------------|-------|
| Chrome | 110+ | Evergreen auto-update |
| Firefox | 110+ | ESR (Extended Support Release) also supported |
| Safari | 16+ | macOS + iOS (mobile-web supported, no native app) |
| Edge | 110+ | Chromium-based |

**IE11:** Not supported. Legacy IE users redirected to browser upgrade notice.

### 4.1.3 Responsive Design

**Desktop-first** (primary); **mobile-web** secondary (min viewport 375px, TanStack Router + MUI responsive grid).  
**Dark mode:** Context-based toggle (localStorage persistence), supports system preference via `prefers-color-scheme` media query.

### 4.1.4 Key UI Components

- **Navigation:** TanStack Router v1.x lazy-loaded routes (code splitting)
- **Tables:** Virtual scrolling via `@tanstack/react-virtual` v3 (500+ rows, no performance degradation)
- **Forms:** React Hook Form + Zod validation, auto-generated via Orval from OpenAPI spec
- **Drag-drop:** @dnd-kit/core + @dnd-kit/sortable (form builder, workflow canvas reordering)
- **State Management:** Zustand (client state) + TanStack Query v5 (server state)
- **Internationalization:** react-i18next (Vietnamese primary, English secondary)

### 4.1.5 Accessibility Compliance

**Standard:** WCAG 2.1 Level AA target.  
**Implementation:**
- Semantic HTML (`<label>`, `<button>`, `<nav>`)
- ARIA roles and live regions for dynamic content
- Keyboard navigation (Tab, Enter, Escape)
- Color contrast ≥ 4.5:1 (Ant Design default)
- Screen reader support (VoiceOver on macOS, NVDA on Windows)

---

## §4.2 Hardware Interfaces

### 4.2.1 Server Hardware Requirements

**Minimum (Dev/Test):**
- CPU: 4 vCPU (Intel x86_64 or ARM64)
- Memory: 8 GB RAM
- Storage: 50 GB SSD

**Recommended (Production):**
- CPU: 8+ vCPU
- Memory: 16+ GB RAM
- Storage: 200+ GB SSD (database) + 500+ GB object storage (MinIO)

**Network:** Gigabit Ethernet (1 Gbps), redundant NICs for prod (10 Gbps recommended).

### 4.2.2 Client Hardware

**No special hardware required.** Standard commodity PC/laptop with:
- 4 GB RAM minimum
- Modern CPU (2015+)
- Network connectivity (broadband or mobile)

### 4.2.3 Optional Hardware

**Smart Card Reader** — Required only for digital signature operations (external CA provider integration, phase-05b). USB 2.0 compatible reader (e.g., HID Omnikey, Gemalto IDBridge).

### 4.2.4 Network Infrastructure

**Bandwidth:** 100 Mbps minimum (1 Gbps recommended for concurrent users ≥500).  
**Latency:** ≤ 100 ms (RTT) between web client, API server, and database (same datacenter assumed).  
**Firewall:** Bidirectional HTTPS (port 443), optional OIDC discovery (port 5005 dev-only).

---

## §4.3 Software Interfaces

**Ref:** `docs/tech-stack.md`, `docs/system-architecture.md`

### 4.3.1 Database

| Component | Version | Notes |
|---|---|---|
| **SQL Server** | 2022 Community/Developer Edition | `docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=... mcr.microsoft.com/mssql/server:2022-latest` |
| Port | 1433 | Default; TLS 1.3 mandatory (prod) |
| Max connections | 100+ (configurable) | EF Core connection pooling via `ConnectionOptions.MaxPoolSize` |
| Authentication | SQL auth (dev), Windows auth (prod optional) | Always use strong passwords (≥12 chars) |

**Backup & Recovery:**
- Daily full backup + hourly transaction log backup
- Restore RPO ≤ 15 minutes, RTO ≤ 4 hours

**Encryption:**
- Transparent Data Encryption (TDE) mandatory for prod
- Always Encrypted for PII columns (symmetric AES-256)
- Backup files encrypted at rest (MinIO server-side encryption)

### 4.3.2 Cache & Distributed State

| Component | Version | Purpose | Port |
|---|---|---|---|
| **Redis** | 7.x | Session cache, distributed locks (RedLock), pub/sub | 6379 |
| Mode | Standalone / Sentinel / Cluster | Configurable via `Redis:Mode` |
| Authentication | AUTH password (prod) | Redis password ≥ 12 chars, TLS 1.3 |

**Fallback:** If Redis unavailable, `IMemoryCache` (in-process) takes over; distributed locking disabled (degraded perf).

### 4.3.3 Object Storage

| Component | Version | Purpose | Port |
|---|---|---|---|
| **MinIO** | latest (S3-compat API) | File uploads, audit log archival, backups | 9000 (API), 9001 (Console) |
| Protocol | S3-compatible HTTP | `https://minio.internal/api/v1/...` |
| Encryption | Server-side AES-256 default | Transparent to client |
| Bucket Policy | Private + lifecycle rules | Auto-delete logs >30 days (archival after 30 days) |

**Usage:**
- Workflow instance attachments
- Form file uploads
- Audit log archival (yearly)
- Report exports (CSV, Excel, PDF)

### 4.3.4 Message Bus & Event Processing

| Component | Version | Purpose | Port |
|---|---|---|---|
| **RabbitMQ** | 4.x | Async event dispatch (MassTransit) | 5672 (AMQP), 15672 (Management UI) |
| Protocol | AMQP 0.9.1 | Durable queues, dead-letter routing |
| Authentication | Username/password (prod) | Separate user per application |
| Reliability | Message persistence, prefetch=1 | Exactly-once semantics + idempotency keys |

**Fallback (Dev):** `Bus:Transport = InMemory` for local testing without RabbitMQ.

### 4.3.5 Background Job Scheduler

| Component | Version | Purpose | Notes |
|---|---|---|---|
| **Hangfire** | 2.x | Scheduled/recurring jobs + background processing | SQL Server job storage (schema `[hangfire]`) |
| Jobs | ArchiveAuditLogsJob (yearly), AccessReviewJob (quarterly), OutboxProcessorJob (every 5s) | Configurable via Hangfire dashboard |
| Dashboard | `/hangfire` endpoint (AuthServer; `SystemAdmin` role required) | Full control in all environments (requeue, delete, trigger) |

### 4.3.6 Identity & Secret Management

| Component | Version | Purpose | Notes |
|---|---|---|---|
| **ASP.NET Core Identity** | 10.x | User store, role store, token generation | EF Core backed |
| **OpenIddict** | 7.x | OIDC/OAuth2 authorization server | Embedded, on-premises |
| **HashiCorp Vault** | latest | Secret storage (prod) | API key rotation, encryption key management |
| **User Secrets** | built-in | Local dev secrets management | `.user-secrets-id` in `.csproj` |

### 4.3.7 External Identity Providers (Conditional)

| Provider | Purpose | Status | Ref |
|---|---|---|---|
| **VNeID** | Vietnamese e-ID federation | Stub + WireMock.Net mock (dev); pending prod setup | NĐ59 |
| **Google OAuth2** | Social login (optional) | Stub configurable via `Authentication:Google:ClientId` | OpenIddict |
| **Azure AD** | Corporate SSO (optional) | Stub configurable via `Authentication:AzureAd:*` | OpenIddict |

### 4.3.8 File Processing & Scanning

| Component | Version | Purpose | Notes |
|---|---|---|---|
| **ClamAV** | latest | Virus scanning on upload | `IVirusScanner` interface; Metadefender alternative supported |
| **ImageMagick** / **Ghostscript** | latest | Image/PDF thumbnail generation | Optional; deferred to project-specific integration |

### 4.3.9 Logging & Observability

| Component | Version | Purpose | Port |
|---|---|---|---|
| **Serilog** | 4.x | Structured logging (JSON format) | Sinks: Console, File, Seq |
| **Seq** | latest | Centralized log aggregation (dev+staging) | 5341 (HTTP), 5342 (HTTPS) |
| **Prometheus** | latest | Metrics exposition (OpenMetrics format) | 9090 (scrape target) |
| **Grafana** | latest | Metrics visualization | 3000 (web UI) |
| **OpenTelemetry** | latest | Distributed tracing (ready for Phase 2) | Jaeger backend compatible |

---

## §4.4 Communication Interfaces

### 4.4.1 REST API

**Standard:** OpenAPI 3.1 specification (auto-generated via Swashbuckle)  
**Protocol:** HTTPS only (TLS 1.3)  
**Port:** 443 (standard)  
**Base URL:** `https://api.example.gov.vn/api/`

**Authentication:**
- **Authorization Code Flow** (web clients) — OpenIddict OIDC with PKCE
- **JWT Bearer Token** — `Authorization: Bearer <access_token>` header
- **API Key** (M2M) — `X-API-Key: <key>` header (server-to-server)

**Request/Response:**
- **Format:** JSON (UTF-8, `Content-Type: application/json`)
- **Error responses:** RFC 9457 Problem Details (`application/problem+json`)
- **Pagination:** Query params `page`, `pageSize` (cursor-based pagination for large datasets)
- **Filtering:** Query params `search`, `status`, `createdFrom`, `createdTo`
- **Sorting:** Query param `orderBy` (e.g., `orderBy=createdAt,desc`)

**Caching:**
- **ETag headers** — Conditional requests via `If-None-Match`, `If-Match`
- **Cache-Control** — `Cache-Control: max-age=300, public` for read-side projections
- **Idempotency** — `Idempotency-Key` header for write operations (UUID)

**Rate Limiting:**
- **Algorithm:** Token bucket (per tenant, per API key)
- **Limit:** 1000 requests/minute per API key (configurable)
- **Response:** `429 Too Many Requests` + `Retry-After` header

**Versioning:**
- **URL-based:** `/api/v1/`, `/api/v2/` (preferred)
- **Header-based:** `X-API-Version: 2.0` (fallback)
- **Deprecation:** `Deprecation: true`, `Sunset: <HTTP-date>` headers

### 4.4.2 Real-Time Communication

**Protocol:** SignalR 8.x (ASP.NET Core)  
**Transport:** WebSocket (primary), fallback to Server-Sent Events (SSE), then long-polling  
**Port:** 443 (HTTPS WebSocket)  
**Hub endpoints:**
- `/hubs/workflow` — Workflow instance updates, task notifications
- `/hubs/forms` — Form submission events (read-only broadcast to form admins)
- `/hubs/notifications` — User notifications (real-time delivery)

**Authentication:** Bearer JWT token in query string or header `Authorization: Bearer <token>` (WebSocket upgrade).

**Reliability:**
- Message ordering preserved per connection
- Client auto-reconnect with exponential backoff (max 60s)
- Server-side message buffer for offline clients (up to 1000 messages, 5-minute window)

### 4.4.3 Asynchronous Events (Internal)

**Broker:** RabbitMQ (AMQP 0.9.1)  
**Library:** MassTransit 8.x  
**Exchange pattern:** Publish-Subscribe (fan-out exchanges)  
**Message format:** JSON  
**Reliability:** Durable queues + dead-letter routing (DLQ)

**Key events:**
- `WorkflowInstanceCreatedEvent`
- `WorkflowTaskAssignedEvent`
- `FormSubmittedEvent`
- `AuditLogCreatedEvent`
- `UserDeactivatedEvent` (cascade soft-delete downstream)

**Consumer guarantees:** Exactly-once via idempotency key (`Event.IdempotencyId`).

### 4.4.4 Webhooks (Outbound)

**Protocol:** HTTPS POST  
**Port:** 443  
**Content-Type:** `application/json`  
**Signature:** HMAC-SHA256 in `X-Webhook-Signature` header (base64-encoded)  
**Retry policy:** 5 retries with exponential backoff (1s, 2s, 4s, 8s, 16s)  
**Timeout:** 10-second per request

**Webhook events:**
- `workflow.instance.completed`
- `form.submission.approved`
- `user.deactivated`

**Payload schema:** Event metadata + entity snapshot (JSON).

### 4.4.5 M2M Service-to-Service Authentication

**Method:** API Key + HMAC signing  
**Header:** `X-API-Key: <key>` + `X-API-Signature: <HMAC-SHA256(body, secret)>`  
**Key rotation:** Manual (future automation via Vault)  
**Scopes:** Configurable per API key (e.g., `workflow:read`, `audit:read`)

---

## §4.5 Integration Points (Summary)

| System | Protocol | Direction | Authentication | Notes |
|---|---|---|---|---|
| **Frontend (React)** | HTTPS REST + SignalR | Bidirectional | JWT Bearer | SPA on separate host/port |
| **OpenIddict AuthServer** | OIDC/OAuth2 | Upstream | N/A (identity provider) | Separate service/process |
| **SQL Server** | TDS (OLEDB) | Downstream | Windows/SQL auth | Local Docker or managed cloud |
| **Redis** | RESP | Downstream | AUTH password | Optional; fallback to IMemoryCache |
| **RabbitMQ** | AMQP | Pub-Sub | Username/password | Optional; InMemory fallback for dev |
| **MinIO** | S3 HTTP | Downstream | AWS credentials | Bucket-level access control |
| **VNeID Provider** | HTTPS REST (SAML) | Upstream | Certificate pinning | Stub only; pending prod integration |
| **CA Digital Signature** | HTTPS REST (PKCS#11 or REST API) | Downstream | mTLS + client cert | Pending QĐ 742 phase-05b |
| **SMTP/SMS Gateway** | SMTP / HTTP | Downstream | Username/password or API key | Notifications module |
| **Hangfire Storage** | TDS (SQL Server) | Downstream | Same as primary DB | No separate auth |

---

## Summary

**16 software dependencies** (core) + **6 optional extensions** (VNeID, CA, email, Elasticsearch, data warehouse).

**Network diagram:**
```
┌─────────────────┐
│   Browser/SPA   │
└────────┬────────┘
         │ HTTPS REST + SignalR
         ▼
┌──────────────────────┐       ┌─────────────┐
│   AqtCoreFW API      │──────▶│ SQL Server  │
│  (ASP.NET Core 10)   │       │    2022     │
├──────────────────────┤       └─────────────┘
│ • 16 Modules         │
│ • 71 Controllers     │       ┌──────────┐
│ • 356+ Handlers      │──────▶│  Redis   │
└──────────────────────┘       └──────────┘
         ▲
         │ Auth/OIDC         ┌──────────────┐
         │                   │ RabbitMQ     │
         ▼                   │ (async bus)  │
┌──────────────────┐         └──────────────┘
│ OpenIddict Auth  │
│   (port 5005)    │         ┌──────────────┐
└──────────────────┘    ┌───▶│    MinIO     │
                        │    │(S3-compat)   │
                        │    └──────────────┘
                        │
                  ┌─────┴─────────┐
                  │               │
            ┌─────────────┐  ┌──────────┐
            │  Hangfire   │  │  VNeID   │
            │   (Bg jobs) │  │(federation)
            └─────────────┘  └──────────┘
```

---

**Last Updated:** 2026-05-25 (v2.137p113)  
**Maintained by:** AqtCoreFW Infrastructure Team
