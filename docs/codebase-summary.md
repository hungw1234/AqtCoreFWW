# Codebase Summary

AqtCoreFW is a production-ready .NET 10 modular monolith for Vietnamese government projects implementing Clean/Onion architecture + CQRS + DDD.

**Status:** v2.137p123+ (2026-06-25) — **Signature Enrollment Admin Approval Phase-3 SHIPPED** (binding-guard tightening: enrolled-tier now requires `IsActive && ApprovalStatus == Approved`, closing H1 Pending-can-sign window; 3 admin endpoints `/api/v1/admin/signer-cert-enrollments/pending`, `/{publicId}/approve`, `/{publicId}/reject` [RequiresMfa]; PadesCompletionService C5 re-check). **Phase 2 Self-Service Enrollment SHIPPED (2026-06-25)** (5 user commands/query + 2 endpoints: `/api/v1/me/signature-profiles` CRUD, `/api/v1/me/signature-cert/{challenge,enroll}` + overview; `SignerEnrollmentNonce`, `UserSignatureProfile` entities; multi-provider cert enroll USB+Mysign; self-service always writes Pending until admin approves). **Signing Foundation Phase A SHIPPED** (Signed PDF view/download: `SigningRequest.SourceFileId`, new `ISigningRequestClient` read-contract (5 methods), `/files/{id}/signing-steps` + `/files/{id}/signed` + Forms mirrors, FE `SignedPdfViewer` (react-pdf), Files list `isDigitallySigned` batch, BE 0W/0E + FE vitest 17/17). **Phase 2c-A SHIPPED (2026-06-17)** (Form-generated PDF USB DocFlow signing end-to-end: cross-module contract Identity→Forms, `GenerateAndInitiateSubmissionSigningCommandHandler`, `POST /api/v1/forms/submissions/{publicId}/sign-document` [RequiresMfa], additive `FormSubmission.SigningRequestId`, FE signing flow + mandatory unverified-cert caveat, BE 793/793 + FE 1132/1132). **Phase 2b-3 (2026-06-17):** Forms multi-signer schema + evidence + authoring — `FormTemplateSignerRole`/`FormSubmissionSignerEvidence` child tables, FE authoring endpoints, fixed 2b-2 migration `NOT IN`→`IN (0,1)` filtered-index SQL Server syntax error, Forms.Integration 21/21 + Identity.Integration 269/269. **Phase 2b (2026-06-17):** Multi-signer transport + BP signing-session aggregate — 2b-1 incremental-chaining (SignerSequenceNumber), 2b-2 BP sign-document gate + `DocumentSignatureSession` (15 tests, Identity.Application 318/318). **Phase 3 (2026-06-16):** Detached data signing closed as already-shipped (verified via Phase-0 C1 `SignerBindingGuard` + detached verify-at-ingest). **Phase 2a (2026-06-16):** USB DocFlow bridge — `ISigningExecutionStrategy` seam, `SignatureOrchestrator.RequestSignatureAsync` → Result, `DocFlowSigningStrategy` executes via `InitiateDocFlowCommand`, `/api/signing/request` + status poll (read-time projection), FE hooks. **Prior: v2.137p119 (2026-06-12) — Per-User CCCD Signing Identity SHIPPED** (ApplicationUser.SigningNationalId encrypted column; migration 20260612150000; SignPayloadCommandHandler resolves signer CCCD → provider user_id; admin user create/edit surfaces CCCD (Confidential PII gated to IDENTITY.USER.UPDATE); FE user form modal; 4 commits, Identity.Integration tests added). **Prior: v2.137p118 (2026-05-26)** — **T3-API-ENVELOPE-FE-RENDER SHIPPED** (Orval regen + structured graph-violation parser + toast render + 3 i18n keys; FE consumer, v2.137p118); **RT2-15 Phase 05 FU-M1 SHIPPED** (cross-mode validator matrix sweep + anti-tautology test; test-only, v2.137p116); **T3-API-ENVELOPE Slice B SHIPPED** (ApiError.Metadata opt-in wire format with PublicMetadata extraction pattern; all endpoints affected, v2.137p117). **Prior: v2.137-QD742-PHASE05B (2026-05-11)** — QĐ 742 Phase-04 Data Classification + Phase-05 Supporting UIs + D7 + Phase-06 Vault Key Rotation + Phase-05b Digital Signature (W0-W9) shipped. **Phase-04 (2026-05-10):** IPepperResolver + VaultPepperResolver (aqt/encryption/email-pepper-{tenantId:N} Vault paths), ClassificationRule entity (Audit module), DataClassificationController (/api/v1/admin/compliance/...), ApplicationUser/RtbfRequest PII encrypted (Email/NormalizedEmail/PhoneNumber/CitizenNationalId/SubjectEmail), 4-column EmailHash split (Global Guid.Empty pepper + per-tenant pepper), PlaintextToEncryptedUpgrader CLR-side rekey wired in hosts, FE /admin/data-classification page (Tabs: inventory/rules, Compliance.Manage gate), EncryptedFieldArchitectureGuardTests (D11.5), MultiTenantLoginTests (P0 regression guard), DoNotEncryptAtRestAttribute (annotated RecoveryCodesHash + WebhookSubscription.SecretHash). 13 commits origin/main, 43/43 architecture PASS. **Phase-05 (2026-05-10):** IpFilterRule entity (T1, CIDR-based whitelist) + IpRulesAdminController (CRUD) + IpFilterMiddleware DB fallback + 2 migrations. SecurityHealthAdminController (6 read-only D7 queries: password-policy, mfa-health, lockout-status, sessions-health, audit-log-health, security-metrics). FE: 4 new pages (network-policies, audit-policy, backup-policy, security-health aggregate). Permissions: `SECURITY.IP_RULE.{VIEW,MANAGE}` + `SECURITY.HEALTH.VIEW` (SystemAdmin-only per P0 audit fixup). Tests: 4 BE + audit integration files, 1019+80 LOC. Architecture 43/43, Identity.Integration 113/113 PASS. **Phase-06 (2026-05-10):** IKeyResolver interface + VaultKeyResolver for multi-version key management (aqt/encryption/v{N} mount secret). EncryptedStringConverter refactored (Func<int,byte[]> keyResolver, int currentVersion, dual-decode v{N}: prefix). AuthServer Program.cs wired with AddVaultConfiguration() + ProductionSecretValidator (parity with Api host). Two no-op EF migrations (Identity + Files): AddCipherVersionPrefix (schema-ready for v{N:...} prefixing). NEW infrastructure types: VaultHealthCheck, ProductionSecretValidator, SecretMaskingDestructuringPolicy, LegacyCipherUpgrader. NEW docs: `docs/ops/vault-key-rotation-runbook.md` (~250 LOC). Vault container added to docker-compose.yml (`hashicorp/vault:latest`). Backwards-compatible (LegacyCipherUpgrader handles v0 ciphertexts). Architecture 9/9 PASS (Qd742Phase06ComplianceTests). Code-review APPROVE. **Phase-05b Digital Signature (2026-05-11):** W0-W9 full deployment — VGCA USB token (Tier-1 raw + Tier-2 XML/JWS + Tier-3 doc-flow) + Viettel Mysign (3-mode: sync/async-poll/async-callback). Interfaces: `IDigitalSignatureProvider`, `IStructuredSignatureService`, `IDocumentSigningWorkflow`. Enums: `SignatureProviderCapabilities` (10 flags), `SignatureFormat` (Raw/Pkcs7/XmlDsig/Jws), `OcspMode` (Soft/Hard). Entities: TenantSignatureConfig (T2), SigningDocFlowRecord/Nonce (T3), SigningPendingTransaction (T3), MysignInboundToken (T3). 13 controllers + 28 E2E specs. Architecture 87/87, Identity.Integration 169/169 PASS. Compliance dossier mục 5.1 = 40 MET + 1 PARTIAL (NEAC root seeding deferred). Docs: integration-guide.md (reference, 16 sections), usage-cookbook.md (8 recipes), spec-amendments.md (D17-D19).

## Quick Stats

| Metric | Value | Notes |
|--------|-------|-------|
| **Backend Modules** | 15 | Identity, Audit, Files, Notifications, Organization, Forms, Workflow, Reporting, AI, MasterData, SystemParams, Integration, Collaboration, Rules (16th: Chat deferred) |
| **Frontend Pages** | 37+ features | Vite + React 19 + Ant Design 5.x + Form Builder UI + Workflow inbox + Admin redesign + workflow-analytics + workflow-migration |
| **Solution Projects** | 90+ | 47 prod + 10 contract csproj + 42 test |
| **Test Coverage** | 2,234+ tests | 1,822 BE unit + 25 integration test projects + 119 T5-10 migration tests + 559 FE vitest (includes 38 Forms red-team tests, 5 menu filter tests, 20 Direction-A UX tests) + 8 T5-08 E2E Playwright delegation tests + existing E2E suite |
| **Security Audit** | 30/30 fixed (100%) | Groups A-F findings remediated, test evidence in place |
| **Compliance** | QĐ742: 40/40, PDPL: 100% | Consent, RTBF, PII scan, RLS, audit trail active |
| **API Endpoints** | 375+ | REST + SignalR hubs across all modules, includes Workflow (+6 delegation + +6 analytics + +9 migration endpoints) + JIT SSO admin APIs + idle timeout config + user search + Phase-05 IpRules (+5) + SecurityHealth (+6), 26 pages with client-side search |
| **Database** | 169 entities (Phase-04 refactored: 62 T1 + 36 T2 + 10 T3 model + ClassificationRule; Phase-05 adds IpFilterRule T1; v2.137pXX P1 adds WorkflowTransitionAssignment + WorkflowTransitionAssignmentClause + WorkflowTaskCandidateIndex) | SQL Server 2022 EF Core 10, includes JitProviderConfig + WorkflowVersionMigration + WorkflowVersionMigrationItem + ClassificationRule + IpFilterRule + 41 RLS policies + 4-column EmailHash split |
| **RLS Architecture** | TenantSessionContextInterceptor in 15 DbContexts | SESSION_CONTEXT + SQL row-level policies, 40 table policies active |
| **Data Classification** | 22 entity files tagged | Public/Internal/Confidential/Restricted per governance framework |
| **Form Field Types** | 32 types | Text, Email, Phone, Number, Date, RichText, File, Signature, Select, MultiSelect, Checkbox, Radio, and 20+ more |
| **Workflow Features** | 7 Phases + Delegation + Analytics + Migration + Handler Resolution (COMPLETE) | CRUD, conditions (11 operators), notifications, tenant assignment, React Flow designer, parallel branching (AND/OR/N-of-M), SLA/recall/attachments/auto-transition, task delegation (time-bounded, auto-expiry, assignment tracking), dashboard analytics (6 metrics, ECharts, 15-min caching, ARIA labels), version migration (4-eyes approval, state mapping, Hangfire executor, sp_getapplock concurrency), per-transition handler assignment rules + task creation factory + first-task-on-instance-creation. **RT2-15 Process-Handler-Resolution COMPLETE (v2.137p116):** All 7 clause modes live (SpecificUser, Initiator, PreviousHandler, ByPermission, ByRole, ByGroup, ByOrgUnit) — the 4 previously-deferred producers (PreviousHandler/ByRole/ByGroup/ByOrgUnit) now ship alongside the 3 MVP modes. v2.137p70 P1 domain (WorkflowTransitionAssignment entities), v2.137p71 P2 service layer (IWorkflowHandlerResolver + CandidatePrincipalSerializer), v2.137p72 P2b stage-only refactor, v2.137p73-p84 P3 slices (authorization + atomic ETR + fine-resolver), v2.137p85-p88 P4 slices (IDOR hotfix + Claim/Unclaim + Inbox EXISTS-over-nav), v2.137p89 P5a factory extraction (IWorkflowTaskFactory), v2.137p91 P5b instance-creation first-task wiring, v2.137p108-p114 Phase 01-04 producer+read-side per-mode (async-cascade + 4 reader methods). |
| **API Error Envelope** | SharedKernel.Api.ApiError + ApiResponse | RFC 9457 Problem Details + GOV extensions. v2.137p117: ApiError.Metadata field (IReadOnlyDictionary<string, object?>) uses DEFAULT-DENY opt-in via reserved "PublicMetadata" key in FluentResults metadata. Example: GraphValidationFailed surfaces violations array via errors[0].metadata.violations for FE inline feedback (v2.137p118 FE consumer: extractViolations parser + formatGraphViolations Vietnamese toast render in handleSave + 3 i18n keys). |
| **Local Dev Automation** | 4 scripts | 1_run-local-dev (bat/ps1/sh), 2_start-tunnels, 3_setup-tunnel-urls (bat/py), 4_expose-demo |
| **Reverse Proxy Support** | ForwardedHeaders | UseForwardedHeaders middleware (Cloudflare Tunnel, nginx, Docker) on AuthServer + API |

---

## Architecture Overview

**Full-stack:** .NET 10 backend + React 19 frontend + separate OpenIddict AuthServer

```
Browser → React SPA (Vite, Ant Design) → AqtCoreFW.Api (13 core modules)
         ↓ OIDC Auth Code Flow
       AuthServer (OpenIddict 7.4)
```

**Layers per module:**
- Domain (entities, value objects, domain events) → No infrastructure
- Application (CQRS commands/queries, handlers, validators) → Uses FluentValidation + FluentResults
- Infrastructure (EF DbContexts, Dapper, repos, MassTransit) → Persistence + messaging
- Presentation (REST controllers, SignalR hubs) → HTTP layer

---

## Deep-Dive Reference

See **[Modules & Infrastructure Appendix](./codebase-summary/modules.md)** for complete module details (all 15 modules with entities, APIs, cross-module contracts), shared infrastructure architecture (SharedKernel, EF Core, Dapper, caching, messaging), data layer patterns (write/read-side, RLS, data classification), design system specifications, and comprehensive testing framework documentation.

**[📋 SRS — Software Requirements Specification](./srs/README.md)** — Complete IEEE 830 SRS + 73 Cockburn use cases across 16 modules (snapshot commit `a593d719`, v2.137p113).

---

## Key Delivery Items (v2.20 — Form Builder + AI Copilot Complete)

**Form Builder UI (100% Ready)**
- @dnd-kit drag-drop canvas interface with field type palette
- 32 field types: Text, Email, Phone, Number, Date, Time, Select, MultiSelect, Checkbox, Radio, Textarea, RichText, File, Signature, Rating, Slider, Toggle, Location, URL, Autocomplete, Matrix, PageBreak, Hidden, Calculation, Likert, Ranking, Password, Color, Barcode, QR, Section, Description
- Conditional fields with visibility rule engine
- Real-time preview, undo/redo support
- Dual storage: relational (SQL Server tables) + JSON column for dynamic schema

**Forms Enhancements (Phase 2.20 + FE Red-Team Fixes)**
- Conditional Fields: Dynamic show/hide logic, expression evaluation at runtime, nested conditions
- File Upload: S3 integration, client-side chunking, progress tracking, per-field validation (maxSize, allowedExtensions)
- CSV Export: Data transformation, template mapping, bulk operations
- Multi-step Forms: Wizard UI, step navigation, progress tracking, conditional visibility
- Submission Approval Workflow: 3-state machine (pending→approved/rejected), reviewer assignment with consentGiven field
- Template Duplication: Form clone with entity preservation, bulk field copying
- RichText Field: TipTap editor, DOMPurify HTML sanitization (XSS prevention), media embeds
- View Column Management: Create, list, edit, delete views with editable column config
- Signature Canvas: Drawing preservation across container resize events
- Error Recovery: ErrorBoundary fallback to textarea on RichText lazy-load failure

**Frontend AI Copilot (100% Ready)**
- Copilot Sidebar: Collapsible Ant Design Drawer, shared CopilotProvider context, useCopilotChat hook
- 375/375 Vitest tests passing + 220+ Form Builder tests
- scrollIntoView polyfill for CI compatibility
- 35+ FE features with ARIA accessibility + dark mode + i18n vi/en

**Admin Frontend Redesign (2026-03-31)**
- Design System: Semantic GOV_COLORS tokens (navy action blue, 4 status colors, distinct dark mode cards)
- New Components: AdminStatCard (metric + trend), AdminPageHeader (title, actions, stats), AdminTableToolbar (search, filters), AdminContentCard (card wrapper)
- Layout: Header 56px, sidebar 260px (desktop) / 80px (tablet) / 280px drawer (mobile), content max-width 1440px
- Navigation: 25 admin pages → 4 categorized groups (Identity & Access, Content & Workflow, System, Integration), 4-level breadcrumb
- Responsive: useLayoutMode hook with matchMedia (zero re-renders), 3-tier breakpoints (desktop/tablet/mobile)
- Dashboard: Health stats, user/session/job counts, quick actions, activity feed
- Border radius: 8px default, 12px large; elevation shadows 3-level system

**Backend AI Services**
- OCR Document Extractor: OllamaDocumentExtractor service, multimodal vision processing
- ReAct Agent: 4 built-in tools, 5-iteration max
- AI Auto-flagging: 5 risk categories, fail-open design
- TenantSessionContextInterceptor in 16 module DbContexts, SESSION_CONTEXT RLS enforcement
- DataClassification attributes on 22 entity files, SQL RLS policies

**JIT SSO Provisioning (v2.45)**
- External OIDC auth schemes configured in AuthServer via appsettings (OpenID Connect, Google, Microsoft, custom)
- JitProvisioningService: auto-create users on first SSO login (email, fullName, TenantId mapping)
- JitProviderConfig entity: per-provider configuration (enable JIT, domain whitelist, require tenant, auto-create email domain)
- Security: email squatting prevention (RT-01), domain whitelist validation (RT-02), tenant requirement enforcement (RT-03), hourly rate limiting (RT-04)
- Admin APIs: 5 endpoints (CRUD JitProviderConfigs) in Identity module + React admin page (/admin/jit-provider-configs)
- ExternalIdentity + ApplicationUser linked on first login, no email-based auto-linking (security by design)

**Testing Expansion**
- 1,808 BE unit tests (all 21 modules, includes 21 JIT SSO tests)
- 32 BE integration tests (Testcontainers pattern)
- 258+ Forms module tests (220 field validators + conditional logic + approval workflow + 38 FE red-team tests: form-visibility-helpers, form-step-utils, rich-text-sanitize)
- 11 architecture tests covering 6 modules
- 772 Playwright E2E tests (P1 implementation complete: Phases 1-8, 4 cross-module spec files, 120 spec files total, 706 P1 TCs + 307 existing baseline = 1,013 total)
- 20 k6 performance test scripts

**Compliance Framework**
- RLS + data classification for PII governance
- Audit logging on sensitive data access
- RTBF erasure respecting classification levels
- DataRetentionRule enforcement for compliance

---

## Local Development & Tunnel Support (v2.51 — 2026-04-10)

**Automated scripts for local dev environment startup, Cloudflare tunnel management, and reverse proxy support.**

**Local Dev Scripts** (`scripts/` folder):
1. **1_run-local-dev.bat/.ps1/.sh** — Start Auth+API+FE services with health checks (Windows/PowerShell/Unix). PS1 version also auto-starts Docker infra (SQL Server 2022 container `sqlserver` + Redis + RabbitMQ) via `infra/docker/docker-compose.yml`; detects port-1433 conflicts with any lingering native SQL service.
2. **2_start-tunnels.bat** — Launch 3 Cloudflare tunnels (AuthServer, API, FE) in background
3. **3_setup-tunnel-urls.bat/.py** — Auto-configure Vite env + appsettings.json for tunnel mode (5 files updated)
4. **4_expose-demo.bat** — Interactive menu for tunnel tools + public URL display

**Reverse Proxy & Tunnel Support:**
- **ForwardedHeaders Middleware:** `UseForwardedHeaders()` added to both AuthServer and API `Program.cs` for X-Forwarded-For/Proto/Host (Cloudflare Tunnel, nginx, Docker reverse proxies)
- **CORS Hardening:** Added `X-Correlation-Id` and `X-Tenant-Id` to allowed headers in `GovApiPolicy` (InfrastructureRegistration.cs)
- **API baseURL Fix:** `api-client.ts` now correctly injects `BASE_URL ? ${BASE_URL}/api/v1 : '/api/v1'` (was missing `/api/v1` prefix with VITE_API_BASE_URL set)
- **Vite allowedHosts:** Tunnel domain entries added to `vite.config.ts` for HMR
- **Auth CORS Config:** `Cors` section added to AuthServer + API `appsettings.Development.json` for tunnel access

---

## Production Readiness Checklist

- [x] CI/CD pipeline (GitHub Actions, SonarQube SAST, CycloneDX SBOM)
- [x] Docker multi-stage builds with security hardening
- [x] Kubernetes Helm charts (StatefulSet, HPA, probes)
- [x] Vault integration for secrets rotation (24h DB, 7d API keys, 90d JWT)
- [x] Distributed cache (Redis in prod, memory in dev)
- [x] Monitoring (Prometheus metrics, Grafana dashboards, Serilog + Seq)
- [x] Load testing (k6 smoke/baseline/full modes, p95<500ms for read ops)
- [x] Database RLS enforced at SQL layer
- [x] Data classification governance in place
- [x] Security audit complete (30/30 findings fixed)
- [x] WCAG 2.2 AA accessibility
- [x] Mobile-responsive design
- [x] Full i18n (vi, en)

---

## Tech Stack

For the complete technology stack reference including versions, architecture patterns, compliance integrations, and component justifications, see **[./tech-stack.md](./tech-stack.md)**.

---

## Phase Roadmap

| Phase | Version | Focus | Status |
|-------|---------|-------|--------|
| P1-P6 | v1.0 | Monolith foundation (13 modules, DDD/CQRS) | ✓ Complete |
| P7-P8 | v1.5 | Feature flags, contract tests | ✓ Complete |
| P9-P10 | v2.0 | React 19 frontend (20 pages) | ✓ Complete |
| P11 | v2.1 | Profile page, Playwright E2E, WCAG AA | ✓ Complete |
| P12-P13 | v2.2-v2.4 | Case comments, webhooks, admin pages | ✓ Complete |
| P14 | v2.5-v2.7 | Security fixes (35 findings), test expansion | ✓ Complete |
| P15 | v2.8-v2.9 | FE code review, compliance tests (48) | ✓ Complete |
| P16 | v2.10 | Authorization upgrade (6 phases A-F) | ✓ Complete |
| P17 | v2.11 | Test expansion (482 new unit/integration) | ✓ Complete |
| P18 | v2.12 | Core entities & API expansion (22 entities, 10 APIs) | ✓ Complete |
| P19-P20 | v2.13-v2.15 | Phases 3-6 (Views, Search, Dashboard, AI, Governance) + Tier 2 Stabilization | ✓ Complete |
| P21 | v2.18-v2.20 | M12 Integration, AI Copilot (Sidebar, OCR, ReAct, auto-flagging), Form Builder UI, Forms enhancements | ✓ Complete |
| P22 | v2.21-v2.23 | Dynamic Workflow Phases 4-6: Frontend Definition Management (React Flow visual designer), Parallel Branching (5 entities, AND/OR/N-of-M logic), Frontend Inbox & Monitoring (generic instance inbox, dashboard) | ✓ Complete |
| **P23** | **v2.24** | **Dynamic Workflow Phase 7 Complete: E2E tests (28 Workflow), EF concurrency fixes (SaveGraph raw SQL, history tracking, RowVersion retry), SLA/recall/attachments/auto-transition** | **✓ Complete** |
| **P24** | **v2.40** | **Codebase Audit v1 + v2 Remediation: Dapper RLS, Hangfire queue migration, Auth session UX, SignalR auth, 403 notification, FormSettingsPanel closure, ViewManager scope, AddressField IsPii, debounce filter, EF dual provider fix (1,808 tests), vitest/orval upgrade (28 npm vulns), Audit v2 Phases 1-5 (Dapper RLS via sp_set_session_context, OutboxInterceptor, BackgroundJobTenantContext, Copilot SSE auth, open redirect validation, tenant GUID validation, 26 pages client-side search, AI controller tenant spoof, SSRF validation, CaseRepository filter, bulk import limit, report auth, 401 redirect dedup, SignalR ref-counting, 20 pages allSettled, i18n)** | **✓ Complete** |
| **P25** | **v2.50** | **RBAC Roles & Permissions CRUD + Chat UI Redesign: Phase G authorization (role CRUD APIs POST/PUT/DELETE, permission assignment with SoD pre-check, 105 permissions seeded, domain events, IsActive filter, FE admin interface), menu seeding (50+ items, 4 admin groups), FE sidebar API integration (useMenuTree hook, API-backed menu rendering); Telegram-style chat sidebar (LastMessagePreview, UnreadCount, SenderName, optimized OUTER APPLY query, IIdentityModuleClient cross-module integration)** | **✓ Complete** |
| **P26** | **v2.51** | **Dynamic Forms FE Red-Team Fixes (10 fixes): Security (DOMPurify XSS, file upload validation), Correctness (consentGiven, RequiredIf runtime eval, fileId in submission, view column editing), Reliability (ErrorBoundary RichText, signature canvas resize, table render), i18n (75 keys), 38 new tests** | **✓ Complete** |
| **P27** | **v2.54** | **Workflow Dashboard Analytics: 6 analytics queries (state-distribution, processing-time, throughput, sla-compliance, aging, bottlenecks), WorkflowAnalyticsController 6 endpoints, Dapper + 15-min ICacheService caching, 1-year date range guard, ECharts dashboard (pie/line/bar charts), WORKFLOW.ANALYTICS.VIEW permission, Top-5+Others throughput aggregation, ARIA labels** | **✓ Complete** |
| P28 | v3.0+ | Microservices extraction (Phase 17), VNeID/Digital Sig (Phase 18), Advanced observability | → Planned |

---

## Next Steps

1. **Microservices Extraction (Phase 17):** Strangler pattern for Notifications, Audit, Files, Reporting, Organization modules
2. **Real Integrations (Phase 18):** VNeID OAuth2 federation, digital signature service replacement
3. **Distributed Tracing (Phase 19):** OpenTelemetry + Jaeger for production observability

---

## v2.40 Audit Remediation Summary (2026-04-02)

### Audit v1 Fixes
- Dapper tenant isolation (3 queries + 2 controllers)
- Hangfire queue migration + dashboard middleware ordering
- Auth session error UX (onSilentRenewError notification)
- Chat SignalR auth + pagination reset on search/filter
- 403 notification, error boundaries, validateSearch, HangfireAuth fix

### Test Infrastructure & Dependencies
- EF Core dual provider fix (ModuleDbContext.OnConfiguring guard) — 1,736→1,808 tests pass
- vitest 2→4, orval 7→8 — 28 npm vulnerabilities fixed
- FE test coverage expanded to 491 vitest tests

### Medium Edge Cases (M2, M5-M7)
- FormSettingsPanel stale closure, ViewManager scope, AddressField IsPii, debounce filter

### Audit v2 Fixes (Phases 1-5 COMPLETE)
- **Phase 1:** Dapper RLS via sp_set_session_context, OutboxInterceptor event preservation, BackgroundJobTenantContext
- **Phase 2:** Copilot SSE auth token, open redirect validation, tenant GUID validation
- **Phase 3:** 26 pages fetch-all for client-side search
- **Phase 4:** AI controller tenant spoof, SSRF validation, CaseRepository filter, bulk import limit
- **Phase 5:** Report download auth, 401 redirect dedup, SignalR ref-counting, 20 pages Promise.allSettled, i18n

### Post-fix
- Copilot GUID validation, consent allSettled, DapperReadDbConnection connection leak fix

---

## Notes

- Production deployment ready with Vault-managed secrets, K8s StatefulSet, health checks, auto-scaling
- All tests passing (1,808+ BE unit + 491 FE vitest), 0 warnings, 100% security audit remediation
- RLS enforced at SQL layer via SESSION_CONTEXT + Dapper RLS implementation + Vault for RBAC decisions
- Full compliance with Law 91/2025 (PII encryption, RTBF, consent, data classification)
