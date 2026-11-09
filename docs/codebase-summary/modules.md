## Module Directory

> **Deferred-module config flags (2026-07-23, PR #132):** 5 modules — **AI, Collaboration, BusinessProcess, Reporting, Rules** — are deferred from the v1.0 launch scope and gated by `Features:{Name}:Enabled` (default true; missing key = enabled, so dev/test keep everything-on). OFF removes the module at 5 layers via `src/host/AqtCoreFW.Api/DeferredModuleFlags.cs`: DI registration skipped, Presentation assembly removed from controller discovery (→ 404), DbContext filtered from the startup migration set, Collaboration ChatHub unmapped, Reporting recurring job gated + `RemoveIfExists`. FE hides their routes/menus via `GET /api/v1/system/module-flags` (host-local `ModuleFlagsController`) + `ModuleFlagGate`/menu post-filter.

### All 15 Backend Modules - CURRENT (2026-06-25)

#### 1. Identity Module (AqtCoreFW.Identity.*)
- **Layer structure:**
  - **Domain**: User aggregates, consent, delegation, attribute-based rules, groups, data scopes, policy rules, SoD conflicts, menu entities
  - **Application**: CQRS handlers for grant/withdraw consent, delegate/revoke roles, group management, data scope assignment, policy rule evaluation, SoD conflict checking, menu authorization
  - **Infrastructure**: EF Core DbContext, repository implementations (ConsentRepository, DelegationRepository, AttributeRuleRepository, GroupRepository, PolicyRuleRepository), services (IDataScopeService, IPolicyRuleEvaluator, IEffectivePermissionService, IMenuService, ISodConflictChecker, IPermissionVersionService), OpenIddict integration, InProcessIdentityModuleClient for cross-module communication
  - **Presentation**: ConsentController, GroupsAdminController, DataScopeAdminController, PolicyRulesAdminController, MenuController, standard controllers for user/delegation/MFA management

- **Authorization Upgrade (v2.11 — 6 Phases A-F, COMPLETE):**
  - **Phase A: Permission Codes & Enums**
    - PermissionCode enum: MODULE.RESOURCE.ACTION (AUTH.USER.READ, AUTH.ROLE.ASSIGN, etc.)
    - RoleType enum: Admin, Officer, Citizen, Delegate
    - UserGroup, UserGroupMembership, GroupRoleAssignment entities
    - Permission codes seeded at startup via data migration

  - **Phase B: Data Scope Layer**
    - DataScopeType enum: SELF, ASSIGNED, ORG_UNIT, ORG_TREE, CUSTOM_LIST, ALL, BY_FIELD
    - IDataScopeService resolves effective scope per user context
    - ResolvedDataScope DTO (permissions + filtered records)
    - Query filters applied automatically in CommandHandlers/QueryHandlers

  - **Phase C: Policy Rules Engine**
    - PolicyRule entity with condition expression (NCalc language for runtime evaluation)
    - IPolicyRuleEvaluator evaluates expressions against context
    - IEffectivePermissionService combines RBAC + data scope + policy rules
    - PermissionAuthorizationHandler enforces checks in request pipeline

  - **Phase D: Admin APIs (16 endpoints)**
    - GroupsAdminController: Create, List, AddUserToGroup, RemoveUserFromGroup, AssignRoleToGroup, RemoveRoleFromGroup, UpdateGroup, DeleteGroup
    - DataScopeAdminController: AssignDataScopeToRole, RemoveDataScopeFromRole, ListDataScopesByRole, ListDataScopeTypes
    - PolicyRulesAdminController: CreatePolicyRule, UpdatePolicyRule, DeletePolicyRule, ListPolicyRules
    - EffectivePermissionsController: Viewer for debugging permission resolution per user

  - **Phase E: User Delegation Upgrade**
    - DelegationStatus enum: Pending, Approved, Active, Revoked, Expired
    - UserDelegation entity enhanced: scoped role assignment + approval flow
    - IPermissionVersionService (Redis-backed): Version-based cache invalidation on permission changes
    - DelegationExpiryJob (Hangfire): Automated cleanup of expired delegations (runs daily)

  - **Phase F: Advanced Authorization Features**
    - SodConflictRule entity: Segregation of Duties conflict matrix
    - ISodConflictChecker: Validates role combinations before assignment
    - AppMenu entity: Menu definitions per application section
    - MenuRolePermission: Maps menu items to required role + data scope
    - IMenuService: Resolves accessible menus per user (role + scope filtering)
    - MenuController (4 endpoints): ListMenus, GetMenuDetail, AssignMenuRole, CheckSodConflict

- **Key features:**
  - OpenIddict 7.x OIDC/OAuth2 server with MFA enforcement (C-01: LoginPost checks RequiresTwoFactor, ROPC blocks MFA users, new VerifyMfa view)
  - Account lockout signal (QĐ742 §5.2): LoginPost error response includes `lockout_remaining_seconds` field for UI countdown timer
  - RBAC (role-based) + ABAC (attribute-based) + data scope-based authorization
  - Consent management (PDPL Law 91 Art. 11): `ConsentController` endpoints for grant/withdraw consent with domain events
    - `POST /api/identity/consent/grant` — Creates ConsentRecord, publishes ConsentGrantedEvent
    - `POST /api/identity/consent/withdraw` — Revokes ConsentRecord, publishes ConsentWithdrawnEvent
    - Events trigger audit logging and notification workflows
  - Delegation mechanism with circular delegation detection (C-03: DelegateRoleCommandHandler prevents A→B if B→A exists)
  - Token revocation on role changes (C-02: AssignRoleCommandHandler + SyncUserRolesCommandHandler call RevokeTokenCommand on role mutation)
  - MFA/TOTP support (QĐ742)
  - VNeID federation stub (replace with real connector per NĐ59)
  - **Digital Signature Integration (Phase-02 & Phase-03, 2026-06-25):**
    - **Phase 2: Self-Service Multi-Provider Enrollment** — Users enroll certificates (USB VGCA, Viettel Mysign) via `/api/v1/me/signature-profiles` CRUD + `/api/v1/me/signature-cert/{challenge,enroll}` endpoints. Entities: `UserSignatureProfile`, `SignerEnrollmentNonce` (nonce-based challenge-response). Multi-provider dispatch via `switch(Kind)` for USB-raw-verify or Remote-SignPayload. Self-service enrollments write `ApprovalStatus = Pending` until admin approval.
    - **Phase 3: Admin Approval & Binding Guard** — 3 admin endpoints: `GET /api/v1/admin/signer-cert-enrollments/pending`, `POST /{publicId}/approve`, `POST /{publicId}/reject` (all [RequiresMfa]). Enrolled-tier binding guard enforces `IsActive && ApprovalStatus == Approved`, closing the H1 Pending-can-sign window. PadesCompletionService C5 re-checks completion status per binding-guard tier.

- **Cross-Module Communication Contract:**
  - `IIdentityModuleClient` interface (SharedKernel.Contracts) — Defines contract for other modules to call Identity module
  - `InProcessIdentityModuleClient` implementation (Identity.Infrastructure) — In-process synchronous communication
  - Used by Audit module for RTBF personal data erasure

- **Repository pattern** (enforced):
  - Interfaces defined in Domain layer (IConsentRepository, IDelegationRepository, IAttributeRuleRepository, IGroupRepository, IPolicyRuleRepository)
  - Implementations in Infrastructure layer
  - Application handlers depend on interfaces only (no concrete DbContext references)
  - Enables testability and layer separation

#### 2. Notifications Module (AqtCoreFW.Notifications.*)
- Email (MailKit), SMS (webhook), In-app (SignalR)
- Liquid template engine for dynamic content
- 11 GOV-branded templates (seeded at startup)
- Async domain event subscriptions via MassTransit
- Test coverage: 21 unit tests

#### 3. Audit Module (AqtCoreFW.Audit.*)
- **Layer structure:**
  - **Domain**: AuditLog aggregates (append-only), RTBF request aggregates
  - **Application**: CQRS handlers including CreateRtbfRequestCommand + handler (processes data erasure requests)
  - **Infrastructure**: EF Core audit log repositories, Dapper read-side queries, anonymization service
  - **Presentation**: RtbfController (GET/POST endpoints), AuditLogController (query endpoints)
- **Key features:**
  - HMAC-chained append-only logs (NĐ53 compliance)
  - Right-to-be-forgotten (RTBF) data erasure (PDPL Art. 17):
    - `GET /api/audit/rtbf/{id}` — Query RTBF request status
    - `POST /api/audit/rtbf/request` — Submit RTBF erasure request
    - Flow: Validates user → Creates RTBF request → Calls IIdentityModuleClient → Erases PII → Records audit trail
    - Returns: 202 Accepted + requestId for async processing
  - RTBF anonymization of personal data fields
  - Audit trail queryable via read-side Dapper queries
  - Cross-module communication: Uses IIdentityModuleClient (contract from SharedKernel) to call Identity module for PII erasure
- **Test coverage:** 34 unit tests + 4 E2E compliance tests (RTBF submission, status checks, audit verification, anonymization)

#### 4. MasterData Module (AqtCoreFW.MasterData.*)
- Province/District/Ward hierarchies
- Case type enumerations
- Seeded data + Redis caching

#### 5. AI Module (AqtCoreFW.AI.*)
- **Layer structure:**
  - **Domain**: AI request aggregates, vector storage contracts
  - **Application**: MediatR handlers for embedding generation, semantic search, LLM routing
  - **Infrastructure**: Microsoft.Extensions.AI wrapper (OllamaChat, EmbeddingGenerator), QdrantVectorStore integration, AiRoutingService
  - **Presentation**: Controllers for chat/search endpoints with server-sent events (SSE) streaming
- **Key features:**
  - Multi-model LLM support via Microsoft.Extensions.AI abstraction (Ollama, Azure OpenAI compatible)
  - Vector embeddings stored in Qdrant with metadata filtering
  - 4-layer data sovereignty: request isolation, vector encryption, metadata anonymization, audit logging
  - Server-sent events (SSE) for streaming LLM responses
  - Request deduplication to prevent duplicate embeddings
  - RBAC-aware routing: classified documents routed to authorized users only
- **Repository pattern** (enforced):
  - `IAiRequestRepository` — AI request aggregates (queries, responses)
  - `IVectorStoreRepository` — Vector storage contracts (embedding CRUD)
  - Application handlers depend on interfaces only
  - Infrastructure provides Qdrant and EF Core implementations

#### 6. Forms Module (AqtCoreFW.Forms.*)
- **Layer structure:**
  - **Domain**: FormTemplate, FormField, FormFieldOption, FormSubmission aggregates; **FormTemplateVersion** (v2.137p23) immutable snapshot + `FormTemplateVersionFactory` (pure static, JSON envelope v1); enums (StorageMode, FormFieldType, FormTemplateStatus)
  - **Application**: CQRS commands/queries for template management, field publishing, submission creation/queries
  - **Infrastructure**: FormTableMigrator with ChildTables support, data source resolvers (EnumRef, InternalRef, ExternalRef), FormulaEvaluationService, TableFieldDataRepository, FormMaterializationService
  - **Presentation**: Controllers for template CRUD, submission endpoints
- **Key features:**
  - **Dual-mode storage:** StorageMode.Json (SubmissionData column) vs StorageMode.Table (dynamic DDL Dapper table)
  - **Relational field definitions:** FormFields table replaces SchemaJson blob — bilingual labels, optional fields
  - **Dynamic DDL:** `forms.Submissions_{templateId:N}` table + `forms.v_{code}` view created at publish time
  - **Complex field types:** TableField (nested rows), AddressField (3-col expansion), DateRange (2-col expansion), Section/Label/Divider (UI-only, skipped in validation)
  - **Formula fields:** NCalc expression evaluation (computed at read time, never stored)
  - **Reference fields:** EnumRef (C# enum reflection), InternalRef (MediatR cross-module), ExternalRef (HTTP + distributed cache with SSRF protection)
  - **Security:** SQL injection mitigation (GUID+regex column names), enum namespace whitelist, URL validation for ExternalRef
  - **Async DDL creation:** FormMaterializationService (BackgroundService with Channel<Guid> queue)
- **Repository pattern** (enforced):
  - `IFormTemplateRepository` — Template aggregates
  - `ITableFieldDataRepository` — Dynamic table row persistence
  - Application handlers depend on interfaces only
  - Infrastructure provides EF Core and Dapper implementations
- **Test coverage:** 76/76 domain unit tests passing

#### 7. Collaboration Module (AqtCoreFW.Collaboration.*) — Chat & Messaging (M06)

**Layer structure:**
- **Domain**: Conversation, ConversationMember, Message, MessageReadState, Thread aggregates; enums (ConversationType, MemberRole)
- **Application**: CQRS handlers for conversation CRUD, messaging, presence tracking, read state management; DTOs enriched with cross-module data (LastMessagePreview, LastMessageSenderName, UnreadCount, ParticipantNames, SenderName)
- **Infrastructure**: CollaborationDbContext, repository implementations (IConversationRepository with FindDirectBetweenAsync), Dapper queries for bulk operations
- **Presentation**: ConversationController (REST), ChatHub (SignalR for real-time messaging)

**Key features:**
- **Real-time messaging via SignalR** (ChatHub at /hubs/chat with presence updates, typing indicators, read state broadcasts)
- **Multi-user conversations** with conversation types (Group, Direct) and member roles (Owner, Member, Observer)
- **Threaded discussion** — Reply-to message support (ReplyToId field)
- **Read state tracking** — Per-user per-message read status via MessageReadState entity
- **Conversation archival** — Soft-delete with IsArchived flag
- **Enriched DTOs (v2.49 Chat UI Redesign):**
  - **ConversationDto:** Added LastMessagePreview (100-char truncated), LastMessageSenderName, UnreadCount (OUTER APPLY aggregate), ParticipantNames (for Direct convs)
  - **MessageDto:** Added SenderName field for rendering without N+1 queries
- **GetConversationsQueryHandler Optimization:** Single Dapper query with OUTER APPLY for last message + unread count calculation. Bulk user name resolution via IIdentityModuleClient.GetUserInfoByIdsAsync() (cross-module communication, no schema joins)
- **FE Chat Sidebar (Telegram-style):** Displays unread count badge, last message preview, sender name, conversation type icon. Click-to-open modal for contact search via UserSearchController
- **Duplicate Direct guard:** CreateConversationCommandHandler.FindDirectBetweenAsync() prevents duplicate 1:1 chats (returns existing if pair already exists)

**APIs (10 endpoints):**
- POST /api/v1/conversations — Create conversation
- GET /api/v1/conversations — List conversations (paginated, with unread counts)
- POST /api/v1/conversations/{id}/members — Add member
- GET /api/v1/conversations/{id}/messages — Get messages
- POST /api/v1/conversations/{id}/messages — Send message
- POST /api/v1/conversations/{id}/messages/{messageId}/delete — Delete message
- POST /api/v1/conversations/{id}/archive — Archive conversation
- POST /api/v1/conversations/{id}/members/{memberId}/update-presence — Update presence
- GET /api/v1/conversations/{id}/threads — Get threaded messages
- POST /api/v1/conversations/{id}/messages/{messageId}/mark-read — Mark message read

**Cross-Module Communication:**
- Uses IIdentityModuleClient.GetUserInfoByIdsAsync() to resolve user names + department codes (defined in SharedKernel.Contracts, implemented by Identity.Infrastructure)
- Avoids direct schema joins across tenant boundaries; enables future RPC/HTTP in Phase 2 microservices

**Test coverage:** 60 unit tests

---

#### 8. Workflow Module (AqtCoreFW.Workflow.*)
- WorkflowEngine<TState, TAction> state machine
- **Phase 1-3.5:** CRUD + versioning, condition evaluation (11 operators), notification configs, tenant-workflow assignment rules
- **Phase 4:** React Flow visual designer with 14 components (state nodes, transition edges, modals, toolbar, properties sidebar); PUT /definitions/{id}/graph for saving designer state
- Definition activation/deactivation endpoints (POST /activate, /deactivate)
- Workflow assignment rules page with 4-level specificity (SystemDefault < Tenant < TenantAndEntity < TenantEntitySubType)
- Step executor with async task routing, signal wait/resume, parallel/conditional branching
- Integration with MassTransit for async task queues and event publishing

#### 9. Files Module (AqtCoreFW.Files.*)
- MinIO object storage integration
- ClamAV virus scanning (async background job)
- (NĐ68 verification: the dead `IDigitalSignatureService` fake-pass stub was removed 2026-07-23 — 0 callers; real signing lives in Identity. External-CA verification remains a phase-05b item; in-process Signature module removed 2026-05-01)
- File encryption for PII (Always Encrypted columns + C-04: encryption key properly injected via IConfiguration → FileRecordConfiguration, config key renamed from DevKey to FieldKey)
- S3-compatible API

#### 10. Integration Module (AqtCoreFW.Integration.*)
- YARP API gateway routing and composition
- Webhook engine for external HTTP callbacks
- Error code catalog (standardized error responses)
- API Key M2M authentication (SHA-256 hashed, Redis cached)

#### 11. Organization Module (AqtCoreFW.Organization.*)
- Organization hierarchy management
- Department/unit CRUD and caching
- Classification level assignment per department
- Organizational unit tree traversal

#### 12. SystemParams Module (AqtCoreFW.SystemParams.*)
- System-wide configuration parameters
- Dynamic lookup and Redis caching
- Admin-configurable settings (rate limits, feature flags, timeouts)

#### 13. Reporting Module (AqtCoreFW.Reporting.*)
- **Layer structure:**
  - **Domain**: ReportDefinition, ReportExecution, QueryCatalogEntry aggregates; OutputFormat, ExecutionStatus enums
  - **Application**: CQRS commands/queries for report execution, template creation, compliance PDF generation; query catalog management
  - **Infrastructure**: ReportingDbContext, repository implementations, ClosedXML/QuestPDF exporters, Hangfire background jobs, local file storage (MinIO-ready)
  - **Presentation**: Controllers for report dashboard, definitions, executions, and compliance exports
- **Key features:**
  - **KPI Dashboard** endpoint (cached 5 minutes, no query execution)
  - **Report Execution** via async Hangfire jobs (returns 202 Accepted + executionId)
  - **Template-driven export:** ClosedXML for Excel (GOV-styled, 50k row limit), QuestPDF for PDF (GOV-branded Vietnamese)
  - **Query Catalog:** Saved queries with SQL validation (expanded blocklist, @TenantId required, SELECT-only)
  - **SQL Injection mitigation:** Template validation via SqlValidationHelper, parameterized Dapper execution
  - **Path traversal protection:** ResultFilePath not exposed in API responses
  - **Compliance PDF generation** with audit evidence (Admin-only endpoint)
  - **Report polling** via ExecutionStatus query (PENDING → COMPLETED/FAILED)
  - **Report download** with file cleanup (ResultFilePath validated before retrieval)
- **Repository pattern** (enforced):
  - `IReportDefinitionRepository` — Report template storage
  - `IReportExecutionRepository` — Execution history and status tracking
  - `IQueryCatalogRepository` — Saved query persistence
  - Application handlers depend on interfaces only
  - Infrastructure provides EF Core implementations

---

## Shared Infrastructure

### SharedKernel (AqtCoreFW.SharedKernel)
- **Base classes:** Entity<TId>, AuditableEntity, AggregateRoot, ValueObject
- **Common interfaces:**
  - `IRepository<T, TId>` — Generic repository contract
  - `IReadDbConnection` — Dapper-based read-side access
  - `ICacheService` — IMemoryCache (dev) / Redis (prod)
  - `IMessageBus` — MassTransit event publishing
  - `IBackgroundJobService` — Hangfire job scheduling
  - `IWebhookService` — External HTTP callbacks
  - `ICurrentUser`, `ITenantContext` — Request-scoped user/tenant info
  - `IConnectionStringProvider` — Database connection string resolution
- **Domain primitives:** WorkflowEngine<TState, TAction>, DomainEvent, Error, Result<T>
- **Security:** IEncryptionService, Always Encrypted column support
- **Observability:** Serilog structure logging, OpenTelemetry instrumentation

### Infrastructure (AqtCoreFW.Infrastructure)
- **Database:** EF Core 10 DbContext base, Dapper IReadDbConnection implementations
- **Cache:** Redis integration, distributed cache invalidation
- **Messaging:** MassTransit RabbitMQ transport, domain event dispatcher
- **Background jobs:** Hangfire configuration, job definitions, `AlertEvaluationJob` (evaluates `AlertRule`s against Prometheus, dispatches Log/Webhook/Email notifications)
- **Secrets:** HashiCorp Vault client (no plaintext secrets in config)
- **Observability:** Serilog sinks (Seq in dev, cloud in prod); two Prometheus scrape endpoints — `/metrics` (prometheus-net) and `/metrics-otel` (OpenTelemetry Prometheus exporter, added 2026-07-21, serves every `AddMeter`-registered module Meter that `/metrics` cannot read); `AlertingMetrics` (`Alerting/AlertingMetrics.cs`) — self-monitoring counters `alerting_notification_failed_total{channel,reason}` and `alerting_evaluation_failed_total{reason}` for the alert evaluator's own delivery/query failures
- **Middleware:** SecurityHeaders, CorrelationId, RateLimiting, TenantAware behavior

### Backend Refactoring (2026-03-20)
Code quality improvements across 5 oversized C# files (>200 LOC):
- **Modularized files:** Extract utility functions, service classes, domain logic into focused modules
- **Impact:** Improved code maintainability, reduced cyclomatic complexity, better test isolation
- **Verification:** All 533 unit tests + 102 integration tests passing post-refactor

---

## Design System (Institutional Modern - 2026-03-20)

### Frontend UI Enhancements
**Color Tokens (CSS Custom Properties):**
- Navy primary: `#1B3A5C` (sidebar, buttons, links)
- Red accent: `#C8102E` (errors, destructive actions)
- Gold warning: `#F2A900` (badges, non-text only — WCAG AA compliance)
- Light/dark variants for 2-tone theme support

**Typography (Be Vietnam Pro + Inter):**
- Heading scales: H1 38px, H2 30px, H3 24px, H4 20px
- Body: 14px/1.57 line height
- Secondary text: #4A5568 (5.2:1 contrast on light bg — AA compliant)

**Components:**
- **Sidebar:** Gradient background, collapsible hamburger menu (768px breakpoint)
- **Topbar:** Breadcrumb navigation, language switcher, user avatar
- **Buttons:** Navy primary, red danger, ghost default (no inline style overrides)
- **Tables:** Row striping, hover states, responsive scrolling
- **Cards:** Elevation + shadow, 24px padding (Ant Design defaults)

**Dark Mode:**
- Reactive React context (Zustand) + localStorage persistence
- High-contrast variants: light navy #2C5AA0, light red #E63946
- All pages tested: light + dark modes pass WCAG AA

**Motion & Accessibility:**
- Smooth transitions (fade, slide) — respects `prefers-reduced-motion`
- Skip-to-content link on all pages
- Keyboard navigation: Tab/Shift+Tab, Enter/Space, Escape
- ARIA labels + roles on interactive elements
- Screen reader tested (NVDA)

---

## Data Layer Patterns

### Write Side (Commands)
- **Technology:** EF Core 10 with DbContext per module
- **Pattern:** Aggregate-root write-through repositories
- **Concurrency:** RowVersion (optimistic locking) — migrated to all tables (Forms, Identity, Files, Workflow) in v2.9
- **Transactions:** Explicit via DbContext.Database.BeginTransactionAsync()
- **Global filters:** TenantId isolation via `HasQueryFilter()`
- **Soft-delete:** All entities support IsDeleted flag via IEntity interface

### Read Side (Queries)
- **Technology:** Dapper via `IReadDbConnection`
- **Pattern:** Raw SQL for optimized SELECT queries
- **Materialization:** Lightweight DTOs (no change tracking)
- **Caching:** Redis via `ICacheService` decorator on query handlers
- **Full-text search:** SQL Server FTS (default), Elasticsearch optional

### Row-Level Security (RLS) & Data Classification (v2.19)
- **TenantSessionContextInterceptor:** Registered in shared DI + integrated into all 15 module DbContexts. Sets SQL Server SESSION_CONTEXT on every database connection (enables native RLS policies at SQL layer).
- **Global query filters:** EF Core `HasQueryFilter()` enforces TenantId isolation at ORM layer; complemented by SQL RLS for direct queries.
- **Data Classification:** [DataClassification] attributes applied to 19 entity files across 10 modules. PII properties tagged Restricted/Confidential/Internal (integrates with audit + governance modules for automated compliance logging).
- **Modules with classifications:** Identity, Audit, Notifications, Organization, Collaboration, AI, Files, Forms, Webhooks.

---

## Testing Infrastructure

### Test Project Organization
```
tests/
├── Directory.Build.props              # Global usings, shared test config
├── unit/
│   ├── AqtCoreFW.Identity.Tests/     # NSubstitute mocks, in-memory logic
│   └── ... (20 unit tests total)
├── integration/
│   ├── AqtCoreFW.Identity.Integration.Tests/
│   └── ... (WebAppFixture + Testcontainers SQL Server)
└── architecture/
    └── AqtCoreFW.Architecture.Tests/  # NetArchTest.Rules layer enforcement
```

### Test Infrastructure Components

**Global setup (Directory.Build.props):**
- `using Xunit;`
- `using FluentAssertions;`
- `using NSubstitute;`
- Shared ItemGroup for test dependencies (xUnit, NSubstitute, FluentAssertions, Testcontainers)

**Unit test pattern:**
- NSubstitute for mocking repositories, services
- FluentAssertions for readable assertions
- No database or HTTP calls
- Test naming: `{Method}_{Scenario}_{ExpectedOutcome}`

**Integration test pattern:**
- **WebAppFixture:** Wraps WebApplicationFactory<Program>
  - Applies all module registrations
  - Configures TestAuthHandler (replaces JWT validation)
  - Exposes HttpClient, CreateScope() for DI access
- **SqlServerFixture:** Manages Testcontainers SQL Server
  - Fresh container per test class lifecycle
  - Runs all migrations before tests start
  - Disposes container on cleanup
- **TestAuthHandler:** Middleware that shortcuts JWT validation
  - Allows tests to set claims directly: `GetAuthToken(userId, roles, tenantId)`
  - No external auth server required

**Infrastructure Integration Tests (8 projects):**
- **BackgroundJobs.Integration.Tests:** Hangfire job execution, queue validation, retry policies
- **Cache.Integration.Tests:** Redis/IMemoryCache patterns, cache invalidation, distributed cache
- **Concurrency.Tests:** EF Core concurrency (RowVersion, SaveGraph raw SQL, history tracking)
- **Installation.Tests:** Migration validation, schema initialization, seeding
- **Messaging.Integration.Tests:** MassTransit publisher/consumer patterns, RabbitMQ integration
- **Resilience.Tests:** Polly circuit breakers, retry policies, timeout handling
- **Security.Tests:** Authentication/authorization flows, RLS validation, JWT/OIDC patterns
- **TenantIsolation.Tests:** Multi-tenancy enforcement, SESSION_CONTEXT, global filters

**Architecture test pattern:**
- NetArchTest.Rules fluent API
- Asserts Clean Architecture boundaries:
  - Domain ← Application ← Infrastructure ← Presentation
  - Domain has zero outbound dependencies
  - Application depends on interfaces only (ICacheService, not Redis directly)
  - Presentation references only Application (via MediatR)
- Example rule:
  ```csharp
  Types.InNamespace("AqtCoreFW.Identity.Domain")
      .Should()
      .NotHaveDependencyOn("AqtCoreFW.Identity.Infrastructure")
      .Because("Domain must be independent of implementation details");
  ```

### Compliance Test Coverage (New 2026-03-21)

**Cross-Tenant Isolation (7 tests):**
- EF Core global filter validation via Testcontainers SQL Server
- TenantId query filtering verified
- Multi-tenant data segregation confirmed

**Concurrency & Race Conditions (11 tests):**
- RowVersion optimistic locking validation

- Case state machine concurrent transitions
- Conflict detection and resolution patterns

**QĐ742 Compliance (17 E2E tests):**
- Security headers (X-Frame-Options, X-Content-Type-Options, CSP)
- Account lockout enforcement (5 failed attempts → lockout)
- Rate limiting per user
- Password policy (min 12 chars, complexity, expiry)
- Token expiry validation

**PDPL Law 91/2025 Compliance (13 E2E tests):**
- Consent workflows (grant/withdraw)
- Right-to-be-forgotten (RTBF) data anonymization
- Audit trail preservation
- Breach notification readiness

### Comprehensive Test Coverage (Phases 1-3 Complete - 2026-03-23)

**Backend Unit Tests: 574 total** (300 → 574, +274 new)
- **Phase 1: Unit Test Gaps (274 new tests)**
  - 9 new test projects: SharedKernel, Organization, MasterData, SystemParams, Files.Domain, Workflow.Domain, Reporting.Domain, Notifications.Domain, Integration.Domain
  - Identity module: 33 tests (auth flows, consent, delegation, ABAC rules, MFA, permission codes, data scopes, policy rules)
  - Audit module: 34 tests (HMAC chaining, RTBF anonymization, append-only validation)
  - Notifications module: 21 tests (template rendering, async delivery)
  - Forms module: 76 tests (field types, storage modes, formula evaluation, reference resolution)
  - AI module: 18 tests (embeddings, semantic search)
  - Reporting module: 16 tests (SQL validation, PDF generation, compliance exports)
  - Files module: 19 tests (MinIO, ClamAV, encryption key handling)
  - Workflow module: 22 tests (state machine, transitions, parallel branching)
  - Organization module: 15 tests (hierarchy, tree traversal, ZERO → 15 new)
  - SystemParams module: 12 tests (feature flags, caching, ZERO → 12 new)
  - MasterData module: 8 tests (lookups, seeding, ZERO → 8 new)
  - Integration module: 11 tests (YARP routing, webhooks)
  - SharedKernel module: 41 tests (validation behavior, tenant awareness, audit behavior, pagination, ZERO → 41 new)

**Backend Integration Tests: 32 total** (0 → 32 new, Phase 2 complete)
- **Phase 2: Integration Test Gaps (32 new tests across 6 projects)**
  - Files Integration (7 tests): Upload, download, delete, validation, virus scanning, cross-tenant isolation, EICAR test
  - Notifications Integration (5 tests): In-app/email persistence, mark as read, template rendering, cross-tenant isolation
  - Organization Integration (8 tests): Create/update/delete org units, hierarchy validation, tree retrieval, cross-tenant isolation
  - Cache Integration (5 tests): Two-tier cache (L1/L2), hit/miss validation, Redis degradation handling, TTL enforcement
  - Messaging Integration (4 tests): MassTransit event publishing, message routing, dead-letter handling, cross-module domain events
  - BackgroundJobs Integration (3 tests): Hangfire job scheduling, execution, cleanup via DelegationExpiryJob pattern

**Frontend Unit Tests: 332 total** (164 → 332, +168 new)
- **Phase 3: Frontend Unit Test Coverage (+168 new tests, 43 files)**
  - Organization: 7 tests (org tree/list view, loading state, error handling)
  - Reports: 9 tests (definitions page, executions page, run/download workflows)
  - Delegations: 8 tests (list view, toggle, create modal)
  - API Keys: 9 tests (list with masked prefix, create modal, delete)
  - System Params: 7 tests (tabs, inline-edit, feature flag toggles)
  - MFA/Profile: 6 tests (user info, change password entry, session management)
  - Webhooks: 6 tests (delivery log page, selector, table states)
  - Workflow: 13 tests (inbox pending tasks, definitions, create modal, admin page, action buttons)
  - Smoke tests: 37 tests across 7 feature areas (Sessions, Backup, ABAC Rules, Access Reviews, AI Search, Profile, Roles)
  - Utilities: Pure function tests for format-date, format-file-size, data transformers
  - Layouts: Responsive sidebar, topbar, breadcrumb navigation tests
  - Hooks: React hook rendering, state updates, side effects (renderHook pattern)
  - Routes: Page smoke renders (each page mounts without crash)

**Architecture & Compliance Tests: 229 total**
- **32 contract tests (Phase 4):** 24 API contract tests (10 files: Case, User, Audit, SystemParams, Organization, MasterData, Notification, Report, Workflow, Files) + 8 event contract tests (2 files: DomainEvent, IntegrationEvent)
- **20 regression tests (Phase 4):** Smoke suite (10 critical API paths) + Regression suite (10 domain operations)
- **69 security tests (Phase 5):** File upload security (16), SSRF prevention (12), security headers (15), error response leakage (10), PII masking (8), RBAC authorization (8)
- **48 compliance tests (QĐ742 + PDPL):** Security headers, lockout signal, consent workflows, RTBF submission, audit trail verification
- **34 authorization tests:** Permission codes, data scope filtering, policy evaluation, group management, SoD conflict detection
- **16 architecture tests:** Clean Architecture layer isolation (NetArchTest.Rules)
- **Testcontainers Docker integration:** SQL Server, Redis, RabbitMQ, MinIO containers for realistic environment

**Frontend E2E Tests: 27 total** (Playwright Docker)
- Browser-based UI: Login flows, page navigation, dark mode, profile workflows, form submission, table operations, CSV export
- SignalR WebSocket: Real-time notification delivery, connection management, group messaging, reconnection handling

**Status (2026-03-23):** All **1,194 tests passing** (100% pass rate). Phases 1-6 complete: backend unit, integration, frontend unit, contract, regression, security, and k6 performance with 23 TCs (p95<500ms read, p95<1000ms write). Remaining: Reliability (Phase 7), A11y (Phase 8), Migration (Phase 9).

---

## Security Audit Results

### Summary
- **Total Findings:** 30 security issues identified across OWASP Top 10 + government standards
- **Status:** 30/30 remediated (100%) — All groups complete
- **Categories:** 5 CRITICAL + 9 HIGH + 12 MEDIUM + 4 LOW
- **Impact:** All CRITICAL and HIGH severity findings fixed and tested

### Groups A-B Findings (18 fixed)
- **5 CRITICAL:** MFA bypass (C-01), token revocation (C-02), encryption key injection (C-04), test auth bypass (F-01), CI/CD config injection (F-03)
- **5 HIGH:** Circular delegation (C-03), Redis crash (C-05), role delegation cap (F-15), clearance cache (F-16), DNS rebinding (F-21)
- **6 MEDIUM:** Test data MFA (F-04), ABAC cache (F-17), password reset revocation (F-09), MFA rate limiting (F-10), audit schema (F-02), delegation ownership (B-02)
- **2 LOW:** Bulk import validation (B-04), soft-delete bypass (E-02)

### Groups C-D Findings (12 fixed)
- 3 HIGH: NCalc guard (C-04), IDOR fixes (C-02), HMAC verification (D-04)
- 5 MEDIUM: Cache invalidation (D-05), error disclosure (E-01), PDF injection (E-03), ClamAV timeout (F-02), virus status (F-03)
- 2 LOW: Quarantine storage (F-04), async validation (F-05)
- 2 MEDIUM: Schema validation (D-01), env var override (D-02)

### Test Evidence (30 findings total)
- 255 new unit tests covering security fixes (Files, Workflow, Forms, Organization, SystemParams, MasterData)
- 66 new FE vitest tests (accessibility, form validation, auth flows)
- 97 new E2E Playwright tests (end-to-end security scenarios)
- Architecture tests enforce layer boundaries (layer isolation prevents privilege escalation)
- Contract tests validate API error codes and domain event schemas

**All 824 tests passing — security audit 100% coverage.**

---

## CQRS & Handler Patterns

### Command Example (CreateCaseCommand)
```csharp
public sealed record CreateCaseCommand(
    Guid TenantId,
    string Title,
    string Description,
    CaseType Type,
    CasePriority Priority) : ICommand<CaseDto>;

public sealed class CreateCaseCommandHandler(
    ICaseRepository repository,
    ICurrentUser currentUser)
    : IRequestHandler<CreateCaseCommand, Result<CaseDto>>
{
    public async Task<Result<CaseDto>> Handle(
        CreateCaseCommand request,
        CancellationToken cancellationToken)
