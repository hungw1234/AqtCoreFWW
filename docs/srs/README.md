---
title: SRS Index — Software Requirements Specification
snapshot_commit: a593d719
snapshot_version: v2.137p113
date: 2026-05-25
---

# SRS — Software Requirements Specification Index

> **AqtCoreFW v2.137p113** — Complete requirements documentation per **IEEE 830-1998** standard.
>
> **Snapshot:** commit `a593d719` (2026-05-25) — **73 use cases** across 16 modules.

---

## 📖 Document Overview

This folder contains the complete SRS for AqtCoreFW, a production-grade .NET 10 backend framework for Vietnamese Government digital transformation projects.

**Purpose:** Single source of truth for:
- **Product scope** — what is built, what is excluded
- **Functional requirements** — 80 detailed use cases (UC-IDN-*, UC-WF-*, UC-FRM-*, UC-BP-*, UC-FIL-*, UC-NTF-*, UC-AUD-*, UC-INT-*, UC-MD-*, UC-AI-*, UC-ORG-*, UC-COL-*, UC-RPT-*, UC-RUL-*, UC-SP-*)
- **Non-functional requirements** — 71 quality attributes (performance, security, reliability, usability, maintainability, portability, compliance)
- **External interfaces** — software, hardware, communication dependencies

---

## 📑 Document Structure

| File | Coverage | Lines | Audience |
|------|----------|-------|----------|
| [srs-overview.md](srs-overview.md) | **IEEE 830 § 1-3** (Intro, Overview, Scope) | 473 | PM, BA, Stakeholders |
| **[use-cases/](use-cases/)** | **Functional requirements** | ~5,500 total | BA, Dev, QA |
| → [uc-identity-core.md](use-cases/uc-identity-core.md) | **13 UCs** (Auth, Login, MFA, Consent, RBAC seeding) | 650 | Identity team |
| → [uc-identity-advanced.md](use-cases/uc-identity-advanced.md) | **11 UCs** (SoD, Clearance, Group/OrgUnit role assignment) | 680 | Identity team |
| → [uc-workflow.md](use-cases/uc-workflow.md) | **14 UCs** (Case creation, state transitions, pooling, reassignment) | 773 | Workflow team |
| → [uc-forms-bp.md](use-cases/uc-forms-bp.md) | **10 UCs** (Form builder, BP designer, submission, approval) | 996 | Forms + BP team |
| → [uc-supporting.md](use-cases/uc-supporting.md) | **32 UCs** (Files, Notifications, Audit, Integration, MasterData) | 714 | Platform team |
| → [uc-platform.md](use-cases/uc-platform.md) | **10 UCs** (AI, Organization, Collaboration, Reporting, Rules, SystemParams) | 467 | Extension team |
| [srs-nfr.md](srs-nfr.md) | **IEEE 830 § 5-6** (Quality attributes, compliance) | 468 | Dev, QA, Infra, Auditor |
| [srs-interfaces.md](srs-interfaces.md) | **IEEE 830 § 4** (External interfaces) | 422 | Dev, Infra, DevOps |

**Total SRS volume:** ~8,600 lines | **Total UCs:** 73 | **Total NFRs:** 71+

---

## 🎯 Reading Guide by Role

### Project Manager
1. **Start here:** [srs-overview.md](srs-overview.md) § 1 (Purpose, Scope, 16 modules)
2. **Then:** [srs-overview.md](srs-overview.md) § 2 (Product functions, constraints, assumptions)
3. **Reference:** [srs-nfr.md](srs-nfr.md) § 5.4 (Availability SLO) + § 6.1 (Legal/Compliance)

### Business Analyst
1. **Start here:** [srs-overview.md](srs-overview.md) § 1.2 (Scope) + § 2.3 (User classes)
2. **Deep dive:** Use-case files per module
3. **Check:** [srs-overview.md](srs-overview.md) § 3 (Specific requirements intro)

### Developer
1. **Start here:** [srs-interfaces.md](srs-interfaces.md) § 4 (REST API, databases, dependencies)
2. **Use cases:** [use-cases/](use-cases/) — skim your module's UCs
3. **Quality attributes:** [srs-nfr.md](srs-nfr.md) — scan NFR-* IDs relevant to your feature
4. **Reference:** [docs/api-design-standards.md](../api-design-standards.md), [docs/code-standards.md](../code-standards.md)

### QA / Test Engineer
1. **Start here:** [srs-overview.md](srs-overview.md) § 2.1 (Product perspective) for context
2. **Test cases:** [use-cases/](use-cases/) — each UC has Acceptance Criteria + Test Scenarios
3. **Quality gates:** [srs-nfr.md](srs-nfr.md) — NFR-MAINT-04 (80% coverage target), NFR-PERF-* (latency targets)
4. **Integration:** [srs-interfaces.md](srs-interfaces.md) § 4.4 (SignalR, RabbitMQ reliability)

### Security / Compliance Auditor
1. **Start here:** [srs-nfr.md](srs-nfr.md) § 5.2 (Security) + § 5.8 (Compliance)
2. **Evidence:** [docs/compliance-evidence-checklist.md](../compliance-evidence-checklist.md) — every NFR-SEC-* maps to control → artifact
3. **Audit log:** [srs-nfr.md](srs-nfr.md) § 5.2.4 (HMAC-chained audit logging)
4. **VN Regulations:** [srs-nfr.md](srs-nfr.md) § 5.2.7 (Law 91, NĐ53/59/68/85, QĐ742 mapping)

### Infrastructure / DevOps
1. **Start here:** [srs-interfaces.md](srs-interfaces.md) § 4.3 + 4.5 (Database, Redis, RabbitMQ, MinIO, Hangfire, deployment)
2. **Hardware:** [srs-interfaces.md](srs-interfaces.md) § 4.2 (Sizing: 4 vCPU/8GB dev, 8+/16GB prod)
3. **SLOs:** [srs-nfr.md](srs-nfr.md) § 5.4 (99.5% availability, 216-minute error budget/month)
4. **References:** [docs/ops/slo-definitions.md](../ops/slo-definitions.md), [docs/deployment-guide.md](../deployment-guide.md)

---

## 📊 Module Coverage Map

| Module | Use Cases | File | UC Count |
|--------|-----------|------|----------|
| **Identity** | Authentication, RBAC, ABAC, SoD, VNeID, Consent | [uc-identity-core.md](use-cases/uc-identity-core.md) + [uc-identity-advanced.md](use-cases/uc-identity-advanced.md) | **24** |
| **Workflow** | Case creation, state machine, task pooling, reassignment | [uc-workflow.md](use-cases/uc-workflow.md) | **7** |
| **Forms** | Form builder, template, submission, approval | [uc-forms-bp.md](use-cases/uc-forms-bp.md) | **6** |
| **BusinessProcess** | BP designer, canvas, form-map orchestration, execution | [uc-forms-bp.md](use-cases/uc-forms-bp.md) | **4** |
| **Files** | Upload, scan, retrieve, archive | [uc-supporting.md](use-cases/uc-supporting.md) | **4** |
| **Notifications** | Email, SMS, in-app, subscription management | [uc-supporting.md](use-cases/uc-supporting.md) | **3** |
| **Audit** | Event logging, chain verification, archival | [uc-supporting.md](use-cases/uc-supporting.md) | **4** |
| **Integration** | Webhook setup, event subscription, outbound dispatch | [uc-supporting.md](use-cases/uc-supporting.md) | **4** |
| **MasterData** | Code management, entity CRUD, caching | [uc-platform.md](use-cases/uc-platform.md) | **3** |
| **AI** | Document classification, recommendation, content generation | [uc-platform.md](use-cases/uc-platform.md) | **3** |
| **Organization** | Tenant, department, team, hierarchy management | [uc-platform.md](use-cases/uc-platform.md) | **2** |
| **Collaboration** | Shared workspace, messaging, activity feed | [uc-platform.md](use-cases/uc-platform.md) | **2** |
| **Reporting** | Ad-hoc query, scheduled report, BI export | [uc-platform.md](use-cases/uc-platform.md) | **3** |
| **Rules** | Rule builder, condition evaluation, action dispatch | [uc-platform.md](use-cases/uc-platform.md) | **2** |
| **SystemParams** | Configuration, feature flags, system settings | [uc-platform.md](use-cases/uc-platform.md) | **2** |
| **Search** | Full-text search (SQL Server FTS or Elasticsearch) | — | **0** |
| **Total** | | | **73** |

---

## 🔗 Cross-Document Links

### From SRS to Implementation
- **Use cases** → [docs/codebase-summary.md](../codebase-summary.md) (module structure, handlers, domain events)
- **NFR-SEC-*** → [docs/compliance-evidence-checklist.md](../compliance-evidence-checklist.md) (control mappings)
- **NFR-PERF-*** → [docs/ops/slo-definitions.md](../ops/slo-definitions.md) (SLO targets, Prometheus queries)
- **Interfaces § 4.3** → [docs/tech-stack.md](../tech-stack.md) (version pins, configuration)
- **UI/UX design** → [docs/design-guidelines.md](../design-guidelines.md) (Ant Design, dark mode, i18n)
- **API contracts** → [docs/api-design-standards.md](../api-design-standards.md) (REST, OpenAPI, versioning)
- **Architecture** → [docs/system-architecture.md](../system-architecture.md) (module map, layers)
- **Dependencies** → [docs/dependency-map.md](../dependency-map.md) (entity relationships, FE↔BE contracts)

### From Implementation back to SRS
- Code pull requests referencing UC-* IDs (e.g., `UC-WF-05 claim task`) auto-link to use-case spec
- Security audit checks reference NFR-SEC-* mappings to control evidence
- Performance regression tests validate NFR-PERF-* targets

---

## 📋 Use-Case Template Reference

Every use case follows this structure (Cockburn fully-dressed):

```markdown
## UC-MODULE-NN: Use Case Title

**Primary Actor:** Role name  
**Preconditions:** System state before action  
**Main Success Scenario:**
1. Actor performs action
2. System validates & responds
3. ...

**Alternative Scenarios:**
- A1: Actor cancels
- A2: System error (timeout, permission denied)

**Postconditions:** System state after success  

**Non-Functional Constraints:**
- Latency: <200ms (p95)
- Availability: 99.5% uptime
- Security: TLS encryption required

**UI/API:**
- Endpoint: `POST /api/v1/workflow-instances/{instanceId}/tasks/{taskId}/claim`
- FrontEnd: [ClaimTaskModal.tsx](../../web/src/features/workflow/modals/ClaimTaskModal.tsx)
```

---

## 🛠️ Maintenance & Versioning

### Update Protocol

**When to update SRS:**
- Controller/handler endpoint added/removed/renamed → update corresponding UC
- Domain entity schema changed → update affected UCs
- Security policy tightened → update NFR-SEC-*
- Performance regression → evaluate NFR-PERF-* compliance

**Update ceremony:**
1. Modify use-case file(s) or NFR/Interface files
2. Bump `snapshot_version` (v2.137p113 → v2.137p114) in frontmatter
3. Update `snapshot_commit` hash to current commit
4. Update `date: YYYY-MM-DD`
5. Commit message: `docs(srs): UC-MODULE-NN {feature} description (vX.YYYpZZZ)`

**Review cadence:**
- **Quarterly:** Re-count UCs per module, validate still accurate
- **Per major release:** Full audit of scope, add/remove UCs as product evolves
- **Per compliance change:** Update NFR-SEC-* + evidence links

### Ownership
- **Content:** AqtCoreFW BA team + domain experts
- **Versioning:** Orchestrator agent (main branch) + docs-manager subagent (per-phase updates)
- **Audit:** Internal security team (NFR-SEC-*), compliance officer (§ 5.2.7 VN regulations)

---

## 📞 References & External Links

| Reference | Purpose | Location |
|-----------|---------|----------|
| IEEE 830-1998 | SRS standard template | [IEEE.org](https://www.ieee.org) |
| Cockburn Use Cases | UC writing methodology | [Alistair.Cockburn.us](http://alistair.cockburn.us) |
| Vietnamese Law 91/2025 | PDPL — Personal Data Protection | VN National Assembly |
| NĐ53/2023 | Cybersecurity Decree | VN Government |
| NĐ59/2023 | VNeID Federation | VN Government |
| QĐ742/2024 | Government IT Security Standard | VN Government |
| OpenAPI 3.1 | API specification | [spec.openapis.org](https://spec.openapis.org) |
| IEEE 25010 | Quality model (ISO/IEC) | [IEEE.org](https://www.ieee.org) |

---

## 🔒 Document Security

**Classification:** Internal (AqtCoreFW project, AEQUITAS BA team)  
**Retention:** Live document; supersedes older versions upon commit merge to main branch  
**Access:** Git repository; no external distribution without approval

---

**Last snapshot:** 2026-05-25 (commit `a593d719`, v2.137p113)  
**Phase:** 08 / 08 (SRS complete)  
**Status:** ✅ Ready for Q2 review + compliance audit
