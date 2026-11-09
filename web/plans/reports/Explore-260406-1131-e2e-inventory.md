# AqtCoreFW E2E Test Planning Inventory

**Date:** 2026-04-06 | **Modules:** 17 | **Total E2E Tests:** 234 | **Total Endpoints:** 304

## EXECUTIVE SUMMARY

- **Total Backend Endpoints:** 304 across all modules
- **E2E Tests Covering Endpoints:** 234 (77% coverage)
- **Untested Modules:** 5 critical modules with 0 tests
- **API vs Browser Split:** 134 API tests (57%) vs 81 browser tests (35%)
- **Real-time/WebSocket Tests:** 0 (major gap)

---

## MODULE COVERAGE TABLE

| Module | Controllers | Endpoints | E2E Tests | Coverage | Status |
|--------|-------------|-----------|-----------|----------|--------|
| identity | 17 | 64 | 40 | 62% | Well-tested but gaps in UI |
| workflow | 4 | 30 | 37 | 123% | Comprehensive API, no designer UI |
| forms | 6 | 31 | 25 | 81% | Good overall, template logic gaps |
| systemparams | 3 | 11 | 23 | 209% | Over-tested, good coverage |
| audit | 4 | 13 | 13 | 100% | API complete, UI gaps |
| masterdata | 4 | 25 | 6 | 24% | Weak, read-only focus |
| files | 4 | 13 | 8 | 62% | Upload UI not tested |
| reporting | 3 | 15 | 7 | 47% | Dashboard UI untested |
| organization | 2 | 9 | 6 | 67% | Tree builder untested |
| notifications | 2 | 10 | 6 | 60% | Inbox UI untested |
| cases | 2 | 11 | 3 | 27% | Status transitions untested |
| integration | 3 | 14 | 2 | 14% | Webhook testing untested |
| ai | 4 | 13 | 0 | 0% | **UNTESTED** |
| collaboration | 2 | 10 | 0 | 0% | **UNTESTED** |
| rules | 3 | 11 | 0 | 0% | **UNTESTED** |
| search | 2 | 7 | 0 | 0% | **UNTESTED** |
| signature | 1 | 7 | 0 | 0% | **UNTESTED** |
| **TOTAL** | **62** | **304** | **234** | **77%** | |


---

## CRITICAL GAPS BY PRIORITY

### TIER 1: ZERO TESTS (5 MODULES)

**1. COLLABORATION (0 tests, 10 endpoints)**
- Controllers: ConversationsController, MessagesController
- Routes: /chat
- Gaps: Chat message CRUD, real-time delivery (WebSocket), threading
- Business Impact: Chat feature completely unvalidated

**2. RULES (0 tests, 11 endpoints)**
- Controllers: RuleSetsController, RuleEvaluationController, DecisionTablesController
- Routes: /admin/rules, /admin/rules/{id}
- Gaps: Rule CRUD, decision tables, rule evaluation testing, versioning
- Business Impact: Business rules engine not verified

**3. SEARCH (0 tests, 7 endpoints)**
- Controllers: SearchController, SavedQueriesController
- Routes: /search, /ai-search
- Gaps: Unified search, advanced filtering, saved queries, AI search
- Business Impact: Search feature not validated

**4. SIGNATURE (0 tests, 7 endpoints)**
- Controllers: SignaturesController
- Routes: /signatures
- Gaps: Document signing UI, multi-party workflows, verification, audit
- Business Impact: Digital signature workflows untested

**5. AI (0 tests, 13 endpoints)**
- Controllers: AiController, AiExtractAgentController, AiModelProfilesController, AiPromptTemplatesController
- Routes: /admin/ai, /copilot
- Gaps: AI invocation, extraction workflows, model profiles, prompt templates, copilot
- Business Impact: AI features completely unvalidated

---

### TIER 2: LOW COVERAGE (<25%)

**INTEGRATION: 2 tests / 14 endpoints = 14%**
- Current: admin-integration.spec.ts (2 browser tests)
- Gaps: Partner registration, contract CRUD, webhook testing, message delivery retry, API key management

**MASTERDATA: 6 tests / 25 endpoints = 24%**
- Current: masterdata-lookups.spec.ts (6 API tests, read-only)
- Gaps: Hierarchy CRUD, bulk import, external mapping, effective dating, validation rules

**CASES: 3 tests / 11 endpoints = 27%**
- Current: crud-cases.spec.ts (3 API tests)
- Gaps: Case detail UI, status transitions, reassignment, public API, filtering

---

### TIER 3: PARTIAL COVERAGE (40%-70%)

**ORGANIZATION: 6 tests / 9 endpoints = 67%**
- Gaps: Org unit tree builder UI, staff assignment, bulk import, cascading permissions

**FILES: 8 tests / 13 endpoints = 62%**
- Gaps: File upload UI (drag & drop), preview rendering, version history UI

**IDENTITY: 40 tests / 64 endpoints = 62%**
- Gaps: ABAC rule builder UI, SOD enforcement, Policy rule editor, Data scoping UI, credential policies

**NOTIFICATIONS: 6 tests / 10 endpoints = 60%**
- Gaps: Inbox UI, template builder, variable substitution, scheduling

**REPORTING: 7 tests / 15 endpoints = 47%**
- Gaps: Dashboard widget rendering, customization, parameter selection UI, export formats

---

## TEST TYPE DISTRIBUTION

```
API-Only Tests:       134 tests (57%)
Browser Tests:         81 tests (35%)
Cross-Module Tests:    21 tests (9%)
WebSocket Tests:        0 tests (0%)   [CRITICAL GAP]
```

---

## WEBSOCKET / REAL-TIME FEATURES (0 TESTS)

Features completely untested:
- Notification delivery (real-time updates)
- Chat message delivery (conversation push)
- Workflow task assignment (inbox updates)
- Audit log streaming (login events)
- File upload progress (stream updates)

---

## RECOMMENDATIONS (PRIORITY ORDER)

### Phase 1: Zero-Test Modules (Weeks 1-2)
1. collaboration.spec.ts — Chat CRUD, threading, real-time (10+ tests)
2. rules.spec.ts — Rule CRUD, evaluation, decision tables (10+ tests)
3. search.spec.ts — Unified search, saved queries (8+ tests)
4. signature.spec.ts — Signing, verification, audit (8+ tests)
5. ai.spec.ts — AI invocation, profiles, prompts (10+ tests)

### Phase 2: Strengthen UI Coverage (Weeks 3-4)
1. workflow-designer.spec.ts — Drag & drop, branching
2. forms-template-builder.spec.ts — Conditional logic, reordering
3. identity-access-control.spec.ts — ABAC/SOD/Policy builder UI
4. masterdata-hierarchy.spec.ts — Tree builder, bulk import

### Phase 3: WebSocket & Integration (Weeks 5-6)
1. websocket-real-time.spec.ts — Notifications, chat, workflow push
2. file-upload-ui.spec.ts — Drag & drop, progress bars
3. integration-webhooks.spec.ts — Webhook delivery, retry logic

### Phase 4: Edge Cases & Performance (Weeks 7+)
1. Concurrent operation handling
2. Large dataset performance baselines
3. Error recovery workflows
4. Bulk operations (import, export)

---

## TEST INFRASTRUCTURE

**Framework:** Playwright (Chrome automation)
**Pattern:** Page Object Model (apiContext + authedPage)
**Auth:** Service account via authenticateUser()
**Data:** Unique seeds + API teardown in afterEach
**Concurrency:** Parallel execution
**CI/CD:** GitHub Actions (assumed)


---

## DETAILED MODULE INVENTORY

### AI Module
**UNTESTED | 4 Controllers | 13 Endpoints | 0 Tests**
- AiController (4) — GET /api/v1/ai, POST invoke
- AiExtractAgentController (3) — POST /api/v1/ai/extract
- AiModelProfilesController (3) — GET/POST /api/v1/ai/profiles
- AiPromptTemplatesController (3) — GET/POST /api/v1/ai/prompts

Gaps: AI invocation, copilot flows, profile CRUD, extraction workflows

### AUDIT Module
**100% TESTED | 4 Controllers | 13 Endpoints | 13 Tests**
- AuditLogsController (4) — GET /api/v1/audit/logs, filtering
- SecurityIncidentsController (4) — GET/POST /api/v1/audit/incidents
- RtbfController (4) — POST /api/v1/audit/rtbf (Right to Forget)
- LoginAuditController (1) — GET /api/v1/audit/login

Tests: audit-endpoints (8), admin-security-incidents (5)
Gaps: RTBF UI workflow, audit log export

### CASES Module
**27% TESTED | 2 Controllers | 11 Endpoints | 3 Tests**
- CasesController (9) — CRUD, status transitions
- CasesPublicController (2) — GET /api/v1/public/cases

Tests: crud-cases (3 API tests only)
Gaps: Case detail UI, status transitions, reassignment, filtering

### COLLABORATION Module
**UNTESTED | 2 Controllers | 10 Endpoints | 0 Tests**
- ConversationsController (5) — Create/read conversations
- MessagesController (5) — Send/read messages, threading

Gaps: Chat CRUD, real-time delivery (WebSocket), mentions, attachments

### FILES Module
**62% TESTED | 4 Controllers | 13 Endpoints | 8 Tests**
- FilesController (5) — Upload, download, delete, list
- DocumentTemplatesController (4) — Template CRUD
- FileVersionsController (2) — Version history
- RetentionPoliciesController (2) — Retention management

Tests: crud-files (8 API tests)
Gaps: Upload UI (drag & drop), preview rendering, version history UI

### FORMS Module
**81% TESTED | 6 Controllers | 31 Endpoints | 25 Tests**
- FormTemplatesController (13) — CRUD, clone, publish
- FormSubmissionsController (6) — Submit, list, filter
- PublicFormsController (2) — Unauthenticated access
- ViewsController (5) — List/grid/detail views

Tests: crud-forms (2), form-submissions (5), admin-content (4), 
        public-form-* (14 browser tests)
Gaps: Template builder drag & drop, conditional logic, bulk import

### IDENTITY Module
**62% TESTED | 17 Controllers | 64 Endpoints | 40 Tests**
- UsersAdminController (12) — User CRUD, roles, lock
- GroupsAdminController (8) — Group CRUD, membership
- RolesAdminController (1) — Role definitions
- SessionAdminController (3) — Session management, logout
- AbacRulesAdminController (4) — ABAC policies
- SodRulesAdminController (4) — SOD rules
- PolicyRulesAdminController (4) — Policy rules
- CredentialPoliciesController (5) — Password policies
- DelegationController (5) — Delegations
- AccessReviewController (3) — Access reviews
- MfaController (3) — MFA setup/verification
- ExternalIdentitiesController (5) — SAML/OIDC
- JitProviderConfigsController (5) — JIT provisioning
- ConsentController (3) — Consent management
- MenuController (4) — Dynamic menus
- DataScopeAdminController (4) — Data scoping
- AccountController (1) — Account operations

Tests: crud-users (3), account-change-password (4), admin-access-reviews (4),
       admin-sessions (5), delegations (5), jit-sso-* (6), mfa-flow (4),
       admin-identity-* (9 browser tests)
Gaps: ABAC/SOD/Policy builder UI, data scoping UI, credential enforcement

### INTEGRATION Module
**14% TESTED | 3 Controllers | 14 Endpoints | 2 Tests**
- PartnersController (5) — Partner CRUD
- ContractsController (5) — Contract CRUD
- MessageLogsController (4) — Message audit

Tests: admin-integration (2 browser tests only)
Gaps: Partner setup, contract versioning, webhook testing, message retry

### MASTERDATA Module
**24% TESTED | 4 Controllers | 25 Endpoints | 6 Tests**
- MasterDataController (8) — Hierarchies
- MasterDataAdminController (9) — Admin CRUD
- DictionaryController (6) — Coded values
- ExternalMappingController (2) — Code mappings

Tests: masterdata-lookups (6 API tests, read-only)
Gaps: Hierarchy CRUD, bulk import, effective dating, versioning

### NOTIFICATIONS Module
**60% TESTED | 2 Controllers | 10 Endpoints | 6 Tests**
- NotificationsController (5) — Notification CRUD
- NotificationTemplatesAdminController (5) — Template management

Tests: notifications-crud (6 API tests)
Gaps: Inbox UI, template builder, variable substitution, scheduling

### ORGANIZATION Module
**67% TESTED | 2 Controllers | 9 Endpoints | 6 Tests**
- OrgUnitsController (6) — Hierarchy CRUD
- StaffAssignmentController (3) — Assignment CRUD

Tests: crud-org-units (6 API tests)
Gaps: Tree builder UI, staff assignment UI, bulk import, cascading perms

### REPORTING Module
**47% TESTED | 3 Controllers | 15 Endpoints | 7 Tests**
- DashboardsController (4) — Dashboard CRUD
- ReportsController (8) — Execution, scheduling, export
- WidgetsController (3) — Widget definitions

Tests: reports-execution (7 API tests)
Gaps: Dashboard widget rendering, customization, parameter selection, exports

### RULES Module
**UNTESTED | 3 Controllers | 11 Endpoints | 0 Tests**
- RuleSetsController (5) — Rule definition CRUD
- RuleEvaluationController (4) — Test rules
- DecisionTablesController (2) — Decision tables

Gaps: Rule CRUD, decision table builder, evaluation testing, versioning

### SEARCH Module
**UNTESTED | 2 Controllers | 7 Endpoints | 0 Tests**
- SearchController (2) — Unified search
- SavedQueriesController (5) — Save/retrieve queries

Gaps: Unified search, advanced filters, AI search, saved query CRUD

### SIGNATURE Module
**UNTESTED | 1 Controller | 7 Endpoints | 0 Tests**
- SignaturesController (7) — Sign, verify, audit

Gaps: Document signing UI, multi-party workflows, verification, audit

### SYSTEMPARAMS Module
**209% TESTED | 3 Controllers | 11 Endpoints | 23 Tests**
- SystemParamsController (3) — System settings
- FeatureFlagsController (3) — Feature flags
- AnnouncementsController (5) — Announcements

Tests: admin-system-params (7), admin-infrastructure (5), 
       admin-api-keys (5), admin-system (6 browser tests)
Gaps: Feature flag code evaluation, announcement scheduling, job retry

### WORKFLOW Module
**123% TESTED | 4 Controllers | 30 Endpoints | 37 Tests**
- WorkflowDefinitionsController (12) — CRUD, versioning
- WorkflowInstancesController (6) — Instance creation/progression
- WorkflowTasksController (8) — Task assignment/completion
- WorkflowAssignmentsController (4) — Assignment rules

Tests: workflow-definitions (9), workflow-configs (9),
       workflow-transitions (9), workflow-dynamic-instances (10)
Gaps: Designer drag & drop UI, task inbox filters, error recovery, timers

---

## KEY METRICS

- **Total Controllers:** 62 across 17 modules
- **Total Endpoints:** 304
- **Total E2E Tests:** 234 (77% endpoint coverage)
- **Test Files:** 52 .spec.ts files
- **Route Files:** 50+ .tsx files in web/src/routes/
- **API vs Browser Split:** 134 API (57%) vs 81 browser (35%) vs 21 cross-module (9%)
- **WebSocket Tests:** 0 (critical gap)
- **Untested Modules:** 5 (ai, collaboration, rules, search, signature)
- **Lowest Coverage:** integration (14%), masterdata (24%), cases (27%)
- **Highest Coverage:** audit (100%), workflow (123%), systemparams (209%)

