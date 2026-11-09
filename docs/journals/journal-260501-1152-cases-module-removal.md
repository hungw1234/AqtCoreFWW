---
date: 2026-05-01
type: chore-major
title: Cases module removed — full delete, no replacement
plan: plans/260501-1152-full-delete-cases-module/
branch: worktree-remove-cases-module
---

## Decision

Removed entire Cases module (AqtCoreFW.Cases.*). No replacement module built. Future hồ-sơ-style workflows = compose via generic **Forms + Workflow** primitives; no dedicated Cases bounded context. MasterData `CaseType` entity preserved as lookup data.

## Why

- Product owner decision: Cases module was a demo golden-path, not a production business feature
- All real workflow/approval logic already lives in the generic Workflow module
- Removing the module eliminates 549k LOC of demo/scaffold code and 4 test projects
- Reduction in cognitive overhead for onboarding + maintenance

## Scope

### Deleted — Backend
- `AqtCoreFW.Cases.{Domain,Application,Infrastructure,Presentation}` (4 csproj)
- `AqtCoreFW.Cases.Contracts` shared lib
- `AqtCoreFW.Cases.BenchSeeder` (performance seeder project)
- `src/modules/cases/` directory (entire tree)

### Deleted — Tests
- `AqtCoreFW.Cases.Tests` (unit — 28 tests)
- `AqtCoreFW.Tests.Integration` Cases subset (integration tests in shared project)
- `tests/performance/cases-epsilon-500vu.js`

### Deleted — Frontend
- `web/src/features/cases/` (all pages + hooks + components)
- `web/src/routes/cases-page.tsx` + related lazy routes
- `web/src/core/i18n/locales/{en,vi}.json` — `cases.*` key namespaces (~40 keys each)

### Deleted — DB (schema `cases`)
- Tables: `CaseComments`, `CaseWorkflow`, `Cases`, `CaseCounters`, `OutboxMessages`, `__EFMigrationsHistory`
- Sequences: all in cases schema
- Schema: `cases` (post-deploy ops: `infra/scripts/drop-cases-schema.sql`)

### Wire-ups edited
- `Program.cs`: `AddCasesModule()`, `AddCasesInfrastructure()`, `CasesDbContext.MigrateAsync()`, 5 using directives
- `Api.csproj`: 2 ProjectReference entries
- `AqtCoreFW.slnx`: Cases module folder block (6 projects) + test refs
- `tests/architecture/AqtCoreFW.Architecture.Tests.csproj`: 5 ProjectReference entries
- `tests/architecture/.../AssemblyFixtures.cs`: 5 Assembly declarations + 1 ModuleAssemblies tuple entry
- `tests/architecture/.../CrossSchemaSqlScanner.cs`: 1 schema-map entry (`cases`)
- `E2ETestDataSeeder.Permissions.cs`: Cases permission AddPerms call + 11 dict entries
- `E2ETestDataSeeder.Menus.cs`: Cases menu group + 1 GUID const + 1 menu row + 1 perm grant
- `E2ETestDataSeeder.RolePermissions.cs`: CASES-specific role perm rules
- `Gateway/OpenApiVietnameseTransformer.cs`: `cases` prefix entry
- `Identity.Application/Authorization/PermCodes.cs`: `Cases` static class (11 PermCodes) removed
- `Identity.Security.Tests/PermCodesTests.cs`: count assertion 125 → 114
- `AqtCoreFW.Reporting.Application`: `ICasesModuleClient` injection removed from `GetKpiDashboardQueryHandler` + 5 Dapper Case queries replaced with null/zero fallback
- `AqtCoreFW.Notifications.Application`: `CaseTransitionedNotificationConsumer` removed + consumer scan registration cleared
- `AqtCoreFW.AI.Application`: `search_cases` + `get_case_detail` OllamaReActAgent tools removed (2 tools, ~60 LOC)
- `AqtCoreFW.Workflow.Application`: `CaseProcess` seeder / case-specific workflow seed removed
- FE `app/router.ts`: 2 imports + 2 routes-array entries
- FE `layouts/sidebar-menu.tsx`: 1 menu item + 1 icon import
- FE `core/i18n/locales/{en,vi}.json`: `cases.*` key namespace removed
- `backlogs/backlog.md`: Cases T-codes marked OBSOLETE

### Docs synced
- `docs/dependency-map.md`: Cases module section, ICasesModuleClient row, CaseCreated* events, FE↔BE cases rows, cross-module dep rows (KPI, Workflow seeder, Notif consumer), entity count row
- `docs/codebase-summary.md`: module count 16→15
- `docs/codebase-summary/modules.md`: Cases module section removed
- `docs/codebase-summary/testing.md`: test project list updated
- `docs/codebase-summary/index.md`: module count 17→15
- `docs/system-architecture.md`: module count, Cases API diagram edges, PermCodes example, AI tools list
- `docs/development-roadmap.md`: Cases-related backlog items marked OBSOLETE; removal entry added
- `docs/project-changelog.md`: removal entry added (Phase 5 commit ae44a785)
- `docs/api-reference-vi.md`: Cases section (3.x) removed + subsequent sections renumbered
- `docs/asyncapi.yaml`: 7 Case event entries removed
- `docs/developer-onboarding-guide.md`: EF migration example updated
- `docs/code-standards.md`: module count in layout table updated
- `docs/compliance-evidence-checklist.md`: Cases-related rows N/A
- `README.md` + `docs/README.md`: Modules table row removed + count updated

### Memory
- NEW `cases-module-removal-2026-05-01.md` (project type)
- `MEMORY.md` index entry added

## Preserved (not part of removed module)

- `MasterData` module `CaseType` entity — preserved as reference lookup (independent entity, no Cases module dep)
- `audit.AuditLogs` rows — immutable per QĐ 742 ATTT compliance; `drop-cases-schema.sql` explicitly SKIPS audit schema
- `IPiiAnonymizer` abstraction in SharedKernel — no Cases coupling
- `IEntityStatusEnumContributor` interface (moved to Workflow.Contracts in v2.132p11) — other future contributors may use
- `IEntityStatusEnumRegistry` in Workflow — not Cases-specific
- Historical journals documenting the Cases→Workflow refactor decisions (`journal-260415-1849-cases-workflow-refactor-plan.md`, `journal-260417-*`, etc.) — immutable records
- Form-builder SignatureWidget + `FormFieldType` enum — not Cases module
- Reporting `GetKpiDashboardQueryHandler` — preserved, Cases KPI columns set to 0

## Cross-module impact

| Module | Impact | Resolution |
|--------|--------|-----------|
| Reporting | KPI dashboard lost 2 Cases metrics (TotalCases, CasesThisMonth) | Fallback to 0 in GetKpiDashboardQueryHandler; ICasesModuleClient injection removed |
| Notifications | CaseTransitionedNotificationConsumer removed | Consumer class + MassTransit scan entry removed; no other notification consumers affected |
| Workflow | CaseProcess seeder + Cases-specific WorkflowDefinition seed removed | Generic WorkflowDefinition CRUD API still fully functional |
| AI (Ollama) | 2 OllamaReActAgent tools removed (`search_cases`, `get_case_detail`) | Agent still functional with remaining 2 tools (classify_text, summarize) |
| Architecture tests | AssemblyFixtures -1 entry; CrossSchemaSqlScanner cases schema entry removed | PASS 19/19 post-removal |

## Test counts

**Pre-removal:**
- BE build: 0E/0W
- Tests: 2,234+ total (per codebase-summary.md)
- PermCodes count: 125

**Post-removal:**
- BE build: 0E/0W (verified Phase 6)
- Architecture tests: 19/19 PASS
- Contract tests: 41/41 PASS
- Security tests (incl. PermCodesTests): 89/89 PASS — count now 114
- Regression tests: 12/12 PASS
- FE tsc: 0 errors
- FE vitest: 7 pre-existing failures (workflow-migration, taxonomy, admin-tenant-selector) — none caused by Cases removal
- Integration: 4 Workflow.Definition failures = pre-existing Testcontainer cold-start (SQL Server not running in CI)

## Deployment notes

**REQUIRED post-deploy action:** Run `infra/scripts/drop-cases-schema.sql` against the target SQL Server after deploying this version.

Script actions (all idempotent):
1. Delete `permissions.RolePermissions` rows for CASES.* permissions
2. Delete `permissions.Permissions` rows WHERE Code LIKE 'CASES.%'
3. Delete `identity.AttributeRules` WHERE Resource = 'Case'
4. Delete `forms.Menus` WHERE Code = 'cases'
5. DROP TABLE cases.CaseComments, cases.CaseWorkflow, cases.Cases, cases.CaseCounters, cases.OutboxMessages, cases.__EFMigrationsHistory
6. DROP all sequences in cases schema (cursor)
7. DROP SCHEMA cases

Script deliberately SKIPS `audit.AuditLogs` (QĐ 742 compliance — rows immutable).

## Architecture impact

- BE solution: projects reduced (6 Cases csproj removed from slnx)
- Module count: 16→15 backend modules
- API endpoints: -5 (`/api/v1/cases/*`, `/public/cases`)
- DB schemas: cases schema removed post-deploy
- PermCodes: 125→114 (-11 CASES.* constants)
- ModuleAssemblies registry: -1 entry
- RLS DbContexts: 16→15 (TenantSessionContextInterceptor)

## Cross-refs

- Plan: `plans/260501-1152-full-delete-cases-module/`
- DB ops script: `infra/scripts/drop-cases-schema.sql`
- Prior cases→workflow refactor history: `plans/260415-1737-cases-workflow-refactor/` + journals 260415-1849 through 260417-*

## Lessons

1. Pre-delete red-team grep matrix across 10 vectors caught all wire-up locations — no orphan symbol errors during build. Phase 4 compile pass validated 0E/0W on first attempt.
2. Cross-module impact requires systematic audit: Reporting KPI handler, Notifications consumer, AI tools, Workflow seeder — all flagged in plan's decoupling matrix (Phase 1).
3. PermCodes count drift: the `All_Should_Contain_N_Entries` assertion caught the exact count change (125→114 = 11 CASES.* constants). Test maintained integrity without manual math.
4. Integration test baseline classification critical: 4 pre-existing Workflow.Definition failures (Testcontainer cold-start) not caused by this removal. Documenting baseline prevents false BLOCKED status.
5. `infra/scripts/drop-cases-schema.sql` idempotent pattern (IF EXISTS guards + TRY/CATCH + PRINT row-counts) mirrors Signature precedent — ops team can re-run safely post-deploy.
