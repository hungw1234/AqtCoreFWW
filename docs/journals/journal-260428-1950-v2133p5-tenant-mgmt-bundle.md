# Journal — 2026-04-28 evening — v2.133p5 Tenant Management Bundle

**Branch:** `thiennd/quirky-solomon-86f797` (worktree, uncommitted at journal time)
**Plan:** `plans/260428-1810-tenant-mgmt-and-switch-reload/`
**Trigger:** User typed `/cook` with feedback "Không có quản lý tenant ư? Khi chọn tenant không reload dữ liệu ư?" → escalated to `/cook --auto plan.md` after planner clarified Option A architecture choice.

## What shipped (3 phases)

### Phase 1 — FE tenant-switch reload bug
- Symptom: SystemAdmin switches tenant via `AdminTenantSelector`, request header `X-Tenant-Id` updates per request, but TanStack Query serves cached data for `staleTime=30s` because query keys don't include tenantId.
- Fix: `web/src/core/api/query-config.ts` exports `PRESERVED_KEY_PREFIXES = ['auth','permissions','menu','i18n','session']` + `shouldInvalidateOnTenantSwitch` predicate. `admin-tenant-selector.tsx` calls `cancelQueries({ predicate })` BEFORE `invalidateQueries({ predicate, refetchType: 'active' })` after sessionStorage write. Race fix: in-flight requests aborted before invalidate so they cannot resolve into the new cache key.
- Tests: 10 vitest PASS (predicate + selector + 5 user feature tests).

### Phase 2 — BE Tenant CRUD aggregate (Option A)
- Architectural surprise discovered by planner scout: **NO `Tenant` entity existed** — `ListTenantsQuery` synthesized rows by joining OrgUnit roots + AspNet user counts. User's `--auto` accepted Option A: introduce new aggregate, no FK retrofit on existing `TenantId` columns.
- Domain: `Tenant.cs` (Code IMMUTABLE, Status enum, RowVersion concurrency, AuditableEntity audit) + 2 events + ITenantRepository.
- Application: 3 commands (Create/Update/ChangeStatus), 2 queries (GetById/ListAdmin), permission codes `IDENTITY.TENANT.{VIEW,MANAGE}`.
- Infrastructure: TenantConfiguration, TenantRepository, EF migration `20260428120054_AddTenantsTable` with Designer + Snapshot, `TenantBackfillSeeder` IHostedService idempotent merge from DISTINCT TenantId of `[identity].AspNetUsers ∪ [organization].OrgUnits`.
- Refactored `ListTenantsQueryHandler` reads canonical `[identity].[Tenants]` (Dapper bracket-escape per memory `feedback-mssql-reserved-schema-escape.md`) preserving OrgUnit-root + user-count enrichment for BC with the AdminTenantSelector consumer.
- Presentation: `TenantsAdminController` at `/api/v1/admin/tenants` (GET / GET {id} / POST / PUT / POST {id}/status / DELETE = ChangeStatus(Archived)). Existing `GET /api/v1/admin/users/tenants` retained as deprecation alias.
- Permission seed: `E2ETestDataSeeder.Permissions.cs` adds `IDENTITY.TENANT.{VIEW,MANAGE}`; SystemAdmin role inherits via existing all-perms loop.
- Tests: 36 unit (4 new Tenant handler tests) + 12 integration (`TenantsAdminControllerTests`) + 2 integration (`TenantBackfillSeederTests`) PASS.

### Phase 3 — FE Tenant CRUD page
- New feature module `web/src/features/tenants/` (5 files): types matching BE DTOs verbatim, 5 TanStack hooks with `tenantQueryKeys.all = ['tenants']` (top-level matches Phase 1 invalidation predicate), Antd-based status tag + Drawer form (Code field disabled in edit mode) + list page.
- Route `/admin/tenants` lazy-loaded gated by `IDENTITY.TENANT.VIEW`.
- Sidebar entry "Quản lý tenant" under System category in `admin-menu-entries.ts`.
- 5 vitest PASS (hooks + invalidation behavior).
- i18n keys deferred — Vietnamese inline matches existing admin-page convention.

### Phase 4 — Code review + red-team verify
- code-reviewer score **6/10** (1 P0 + 1 P1 + 4 P2 + minor P3):
  - **P0 (FIXED):** `TenantBackfillSeeder` INSERT used non-existent column `UpdatedBy` (migration created `ModifiedBy` per `AuditableEntity` convention) + missed `IsDeleted/ClassificationLevel` NOT NULL defaults. Test infra masked it because `WebAppFixture.RemoveAll<IHostedService>` strips the seeder. Fixed: column rename + explicit `0,0` defaults.
  - **P1 (FIXED):** `TenantBackfillSeederTests.cs` was missing (R-7 in red-team). Added 2 tests using `IAsyncLifetime.InitializeAsync` per memory `feedback-webappfixture-removes-hostedservices.md` to manually invoke the static `BackfillTenantsAsync` method against a real SQL Server. Both PASS.
  - P2 deferred: R-3 JWT-active tenant suspension policy (TODO in `ChangeTenantStatusCommandHandler`); publisher dispatch inconsistency (Update uses generic `Publish<TEvent>`, Create/ChangeStatus cast `(object)`); backfill multi-instance race acceptable in single-host topology.

## Build + test summary
- `dotnet build src/AqtCoreFW.slnx` → **0 errors / 0 warnings**
- BE Identity unit tests: **36/36 PASS**
- BE Identity integration tests filtered Tenant: **14/14 PASS** (12 controller + 2 backfill seeder)
- FE vitest: **15/15 PASS** (10 Phase 1 + 5 Phase 3)

## Key incidents + lessons learned

### 1. Sub-agent overload mid-implementation
- `fullstack-developer` agent hit Anthropic API `overloaded_error` twice during Phase 2 (after 68 then 95 tool uses). Recovery worked because: (a) prior tool calls had committed durable file changes to disk, (b) spawning a fresh agent with explicit "what's already done" + "what's remaining" context unblocked progress.
- **Lesson:** for long subagent runs, structure prompts so partial completion leaves recoverable state. Never assume sub-agent will run to completion.

### 2. NSubstitute proxy on generic `IPublisher.Publish<TEvent>`
- Initial Tenant entity used `List<object> _domainEvents`. Handler called `publisher.Publish(domainEvent, ct)` → compiler bound to `Publish<object>`. Tests spying on `Publish<TenantCreatedEvent>` saw zero matches.
- Fix: change list to `List<IDomainEvent>` (matches existing codebase convention `Cases.Case`, `Reporting.ReportWidget`). Then explicit cast `(object)domainEvent` at the publish site forces the non-generic `Publish(object, CancellationToken)` overload — MediatR dispatches by runtime type internally. Tests assert via `Arg.Is<object>(e => (e as TenantCreatedEvent) != null && ...)` because Expression-tree matchers cannot use `is` patterns (CS8122).
- **Lesson:** test failure on a publisher receive expectation is almost always overload mismatch. Pick one dispatch style and stick with it across all handlers (publisher dispatch inconsistency between Update vs Create/ChangeStatus is the open P2 follow-up).

### 3. Migration column-name discrepancy hidden by stripped HostedService
- The P0 found in code review was a single-character difference (`Updated` vs `Modified`) in raw SQL embedded in the seeder. Compile-time clean, runtime fail. Tests didn't catch because `WebAppFixture.RemoveAll<IHostedService>` strips the seeder during integration tests, and the `TenantBackfillSeederTests` (R-7) hadn't been written yet.
- **Lesson reinforced:** any IHostedService that mutates state needs an explicit integration test that calls its core logic directly via `IAsyncLifetime` — memory `feedback-webappfixture-removes-hostedservices.md` is real and biting.

### 4. Permission gates require effective-permission service in tests
- `PermissionAuthorizationHandler` queries `IEffectivePermissionService.GetSummaryAsync(userId)` from DB. Integration tests using random `Guid.NewGuid()` userIds → no DB record → 403 Forbidden on every endpoint.
- Fix: added `TestSystemAdminPermissionBypassHandler` to `WebAppFixture.ConfigureTestServices` — additive auth handler succeeds any `PermissionRequirement` for principals holding the `SystemAdmin` role claim. Production `PermissionAuthorizationHandler` still runs alongside; bypass is `ConfigureTestServices`-scoped only.
- **Lesson:** when production auth depends on DB lookups, integration tests need either full user+role+permission seed or a test-only bypass. The bypass is acceptable here because it ONLY succeeds for an explicit role claim, doesn't deny anything, and lives in test infra.

## Open follow-ups (deferred, tracked in plan + backlog)
- **R-3 / Q2 (P2):** Tenant suspension does not block in-flight JWTs. TODO in `ChangeTenantStatusCommandHandler.cs`. Need policy decision: per-request tenant-status check (immediate block) vs token-expiry wait (eventual). Memory `multi-tenant-deployment.md` says cross-tenant isolation is P0 in prod — this gap should be addressed before tenant suspension is used in anger.
- **Publisher dispatch (P2):** Three command handlers, two patterns (`Publish<TEvent>` vs `Publish((object)e)`). Pick one.
- **Backfill multi-instance race (P2-3):** Idempotent merge logic is safe for single-host; with multiple replicas booting simultaneously two seeders could both pass the EXISTS check and INSERT — second INSERT fails on unique Code constraint. Accepted in current topology.
- **Migrate AdminTenantSelector to canonical endpoint:** still uses legacy `GET /admin/users/tenants` alias. Switch to `GET /admin/tenants` in a follow-up FE PR.
- **i18n keys:** Vietnamese hardcoded in tenant-list-page + form-drawer + status-tag. Move to `vi-VN/common.json` + `en-US/common.json` if/when EN locale is enabled.

## Files touched (uncommitted)
**FE (Phase 1+3):**
- M `web/src/core/api/query-config.ts` (predicate + allowlist)
- M `web/src/core/api/index.ts` (re-export)
- M `web/src/shared/components/admin-tenant-selector.tsx` (cancel + invalidate)
- A `web/src/features/tenants/{tenant-types,tenant-api,tenant-status-tag,tenant-form-drawer,tenant-list-page,index}.{ts,tsx}`
- A `web/src/routes/admin-tenants-page.tsx`
- M `web/src/app/router.ts` (route registration)
- M `web/src/layouts/admin-menu-entries.ts` (sidebar entry + GlobalOutlined import)
- A `web/src/test/core/api/query-config-predicate.test.ts`
- A `web/src/test/shared/admin-tenant-selector.test.tsx`
- A `web/src/test/features/tenants/use-tenants.test.ts`

**BE (Phase 2):**
- A Domain: `Tenant.cs` + `TenantStatus.cs` + 2 events + `ITenantRepository.cs`
- A Application: 3 commands × 3 files + 2 queries × 3 files
- M `Authorization/PermCodes.cs` (Tenant.View/Manage)
- A Infrastructure: `Configurations/TenantConfiguration.cs`, `Persistence/TenantRepository.cs`, `Seeding/TenantBackfillSeeder.cs`, migration `20260428120054_AddTenantsTable.{cs,Designer.cs}` + Snapshot update
- M `IdentityDbContext.cs` (DbSet)
- M `InfrastructureRegistration.cs` (DI + AddHostedService)
- M `Queries/ListTenants/ListTenantsQueryHandler.cs` (refactored to canonical table)
- A `Presentation/Controllers/TenantsAdminController.cs`
- M `host/AqtCoreFW.Api/E2ETestDataSeeder.Permissions.cs` (Tenant perm registration)
- A 4 unit handler test files
- A `Controllers/TenantsAdminControllerTests.cs` (12 cases)
- A `Seeding/TenantBackfillSeederTests.cs` (2 cases, post-review P1 add)
- M `tests/shared/AqtCoreFW.Tests.SharedFixtures/WebAppFixture.cs` (TestSystemAdminPermissionBypassHandler additive auth shortcut)

## Docs synced (7-artifact verification)
- ✅ `docs/dependency-map.md` — header v2.133p5 + new Tenant entity row (line 135)
- ✅ `docs/project-roadmap.md` — pinned @ v2.133p5 + new entry (line 10)
- ✅ `docs/project-changelog.md` — v2.133p5 section (line 9+)
- ✅ `backlogs/backlog.md` — header + v2.133p5 entry (line 5)
- ✅ `memory/project-state.md` — frontmatter + body updated
- ✅ `memory/MEMORY.md` — project-state.md row description updated
- ✅ This journal

## Next session
- User decides whether to commit + open PR. If shipping: tag `v2.133p5`; PR title suggestion `feat(identity): tenant CRUD aggregate + tenant-switch reload fix (v2.133p5)`.
- Resolve P2 follow-ups: pick publisher dispatch convention (lean toward keeping the `(object)` cast pattern for runtime polymorphic dispatch); decide JWT-suspension policy.
- Optional FE follow-up: migrate AdminTenantSelector to `/admin/tenants` and deprecate alias.
