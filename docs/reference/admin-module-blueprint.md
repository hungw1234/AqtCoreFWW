# Admin Module Blueprint
**Version:** 1.0 | **Date:** 2026-03-05 | **Context:** AqtCoreFW GOV Framework

Universal checklist for admin/management modules in enterprise and GOV systems. Tier = Must/Should/Nice.

---

## Domain 1: Identity & Access Management

| Module | Tier | Description |
|--------|------|-------------|
| User Management | Must | CRUD, search, lock/unlock, bulk import CSV, export |
| Role Management | Must | Define roles, assign to users, view effective permissions |
| Permission/Attribute Rules | Must | ABAC attribute-based rules, admin-configurable |
| Organization Unit Management | Must | Tree structure, assign users to org units |
| Access Review | Must | Periodic review of user permissions (QĐ742) |
| Session Management | Must | View active sessions, force revoke |
| Login Audit Dashboard | Must | Failed logins, locked accounts, suspicious IPs |
| 2FA Policy Enforcement | Should | Force 2FA for specific roles or org units |
| Password Policy Config | Should | Min length, complexity, expiry rules via admin |
| Consent Record Admin | Must (NĐ13) | View/export consent records per user/purpose |
| RTBF Request Tracking | Must (NĐ13) | Track right-to-be-forgotten requests, processing status |

## Domain 2: Security & Compliance

| Module | Tier | Description |
|--------|------|-------------|
| Audit Log Viewer + Export | Must | Filter by module/user/action, export for compliance |
| Security Incident Management | Must | Record, classify, track security incidents (QĐ742) |
| Data Retention Policy | Must (NĐ53) | Admin configure retention periods per data type |
| IP Allowlist/Blocklist | Must | CIDR-based rules, cached middleware enforcement |
| API Key Lifecycle | Must | Create/rotate/revoke M2M API keys |
| Certificate/Token Key Info | Should | View JWT signing key info, trigger rotation |

## Domain 3: System Configuration

| Module | Tier | Description |
|--------|------|-------------|
| System Parameters | Must | DB-backed key-value config, admin CRUD |
| Feature Flags | Must | Runtime enable/disable features without deploy |
| System Announcements | Must | Global notifications to all users |
| Email/Notification Templates | Must | DB-backed templates, Scriban engine, admin editable |
| Rate Limit Configuration | Should | Runtime adjust rate limits per API key/role/endpoint |
| Translation/i18n Admin | Nice | DB-backed string overrides, no deploy needed |

## Domain 4: Integration & API

| Module | Tier | Description |
|--------|------|-------------|
| Webhook Subscription Admin | Must | View subscriptions, delivery logs, retry, revoke |
| API Version/Sunset Panel | Should | View active versions, deprecated endpoints, sunset dates |
| External Service Status | Should | Health check dashboard for Redis/RabbitMQ/SQL/Qdrant |

## Domain 5: Operations

| Module | Tier | Description |
|--------|------|-------------|
| Background Job Management | Must | Trigger/pause/resume/retry Hangfire jobs via REST |
| Cache Management | Must | Flush by key or module, view stats |
| Bulk Job Tracking | Should | Status of long-running import/export jobs |
| Data Backup Trigger | Should | Admin trigger backup + view backup history |
| Health Dashboard | Should | All service statuses in one admin view |

## Domain 6: Content & Data

| Module | Tier | Description |
|--------|------|-------------|
| MasterData Admin | Must | CRUD for reference data (province, district, category) |
| Dynamic Workflow Admin | Should | Define/edit workflow state machines in DB |
| Dynamic Forms Admin | Should | Create/edit form schemas in DB |
| Reporting Admin | Should | Configure scheduled reports, on-demand export |

## Domain 7: AI (if applicable)

| Module | Tier | Description |
|--------|------|-------------|
| AI Model Configuration | Should | Per-module AI provider override, routing rules |
| Vector Store Management | Nice | View Qdrant collections, index stats, reindex trigger |

---

## Priority Decision Matrix

```
                    Compliance Risk
                    HIGH    |   LOW
                    --------|--------
Business    HIGH |  Must   |  Should
Impact           |         |
            LOW  |  Should |  Nice
```

**Must-have for ANY production system:**
1. User/Role/Org Management
2. Audit Log + Security Incident
3. Session Management + Login Audit
4. Notification Templates
5. Background Job Management
6. Cache Management
7. IP Allowlist/Blocklist
8. Feature Flags + System Parameters

**GOV-specific additions (NĐ13/53/59/QĐ742):**
- Consent Record Admin
- RTBF Request Tracking
- Data Retention Policy
- Access Review
- API Key Lifecycle
- Webhook Subscription Admin
