---
date: 2026-05-01
type: chore-major
title: Signature module removed — pivot to external CA service
plan: plans/260501-1007-delete-signature-module/
branch: worktree-chore-remove-signature-module
---

## Decision

Removed entire in-process Signature module. Future digital signing requirements = external VN CA integration adapter (MISA eSign / VNPT-CA / Viettel-CA / VGCA) implemented in Files module or thin Integration.Signing module. No in-process PKI/HSM/OCSP infrastructure to build.

## Why

- Project owner decision: buy-vs-build favors buy in mature VN CA market
- Self-built PKI rejected as anti-pattern (per `docs/compliance/qd-742-cap-4/decisions-log.md` D1)
- QĐ 742 cấp-4 §5.1 implementable as thin external adapter

## Scope

### Deleted
- BE: `AqtCoreFW.Signature.{Domain,Application,Infrastructure,Presentation}` + `AqtCoreFW.Signature.Contracts` shared lib (5 csproj)
- Tests: 3 projects (Signature.Domain.Tests, Signature.Application.Tests, Signature.Integration.Tests) + `tests/performance/signature-load-test.js`
- DB: schema `signature` (5 user tables + schema-local `__EFMigrationsHistory` + 4 EF migration rows + 0 RLS policies — Docker dev DB only, 0 prod data)
- FE: `web/src/features/signature/` (3 files) + `web/src/routes/signatures-page.tsx`

### Wire-ups edited
- `Program.cs`: 4 using directives + 1 controller AddApplicationPart entry + DI chain (`.AddSignatureModule().AddSignatureInfrastructure(...)`) + `SignatureDbContext.MigrateAsync()` line
- `Api.csproj`: 3 ProjectReference entries
- `AqtCoreFW.slnx`: 1 Folder block (4 projects) + 1 contracts ref + 2 unit-test refs + 1 integration-test ref
- `tests/architecture/AqtCoreFW.Architecture.Tests.csproj`: 4 ProjectReference entries
- `tests/architecture/.../AssemblyFixtures.cs`: 4 Assembly declarations + 1 ModuleAssemblies tuple entry
- `tests/architecture/.../CrossSchemaSqlScanner.cs`: 1 schema-map entry
- `tests/performance/run-all.sh`: 2 invocations
- `E2ETestDataSeeder.Permissions.cs`: 1 AddPerms call + 3 dict entries
- `E2ETestDataSeeder.Menus.cs`: 1 group-comment text + 1 GUID const + 1 menu row + 1 perm grant
- `E2ETestDataSeeder.RolePermissions.cs`: 2 SIGN-specific officer perm rules
- `Gateway/OpenApiVietnameseTransformer.cs`: 1 dict entry
- `Identity.Application/Authorization/PermCodes.cs`: `Sign` static class + `Sign.Doc.*` array entry
- `scripts/seed-permissions.sql`: 4 lines (comment + 3 INSERT rows)
- FE `app/router.ts`: 1 import + 1 routes-array entry
- FE `layouts/sidebar-menu.tsx`: 1 menu item + 1 icon import
- FE `test/features/phase5/smoke-phase5-pages.test.tsx`: 1 comment + 1 vi.mock + 1 import + 1 describe block (4 it cases)
- FE `core/i18n/locales/{en,vi}.json`: 1 nav.signatures key + 1 signatures.* object (~25 keys each)
- FE `shared/components/status-badge.tsx`: 4 status mappings (waiting/signed/declined/expired)

### Docs synced
- `docs/dependency-map.md`: 8 hits (mermaid edge, IFilesModuleClient consumers, ISignatureModuleClient row, contracts assembly, Signature schema section, entity-count row, API map row, integration-tests row)
- `docs/codebase-summary/modules.md`: NĐ68 line + classifications module list
- `docs/codebase-summary/testing.md`: integration tests list (17→16) + project list
- `docs/codebase-summary.md`: backend modules count (17→16)
- `docs/system-architecture.md`: module count (21→20) + Signature PKI Stubs section
- `docs/development-roadmap.md`: Phase 18 Digital Signature Service pivot bullets
- `docs/project-changelog.md`: NEW entry at top
- `docs/api-reference-vi.md`: section 16 removed + section renumbering (16→18 Admin)
- `docs/code-standards.md`: Standard layout module count (16→15)
- `docs/compliance-evidence-checklist.md`: NĐ68 rows updated for external CA pivot
- `docs/compliance/qd-742-cap-4/plan-summary.md`: 2026-05-01 PIVOT banner under Phase 5b architecture

### Memory
- NEW `signature-module-removal-pivot-2026-05-01.md` (project type)
- `MEMORY.md` index entry added
- `project-state.md` frontmatter description + Last Updated section appended
- `qd-742-cap-4-plan-approved.md` D7 amendment appended

## Preserved (not part of removed module)

- Form-builder SignatureWidget canvas pad (`web/src/features/forms/builder/signature-widget.tsx`) + `forms.signature.clear` i18n key + FormFieldType `'Signature'` + react-signature-canvas tests
- Files module ND68 stub (`StubDigitalSignatureService` + `IDigitalSignatureService`) — preserved as integration seam for future external CA adapter
- Historical: `docs/journals/journal-260422-0907-signature-enrollment-pivot.md`, memory `research-vn-pki-digest`, `research-ocsp-ltv-ceremony`, past plan reports

## Test impact

**Pre-removal baseline:** BE build 0E/0W (2:54 elapsed); 822/833 FE vitest pass (11 pre-existing flakes per memory `c5-finalize-blockers.md`).

**Post-removal:**
- BE build: 0E/0W (1:17 elapsed full solution rebuild after PermCodes fix)
- Architecture tests: PASS (exit 0; ModuleAssembliesByName N→N-1)
- Unit tests: previously failing `PermCodesTests.All_Should_Contain_122_Entries` updated to `_125_Entries` (pre-existing 128 drift recovered post-removal to 125 = expected baseline)
- FE tsc: 0 errors
- FE vitest: 822/833 pass (same 11 pre-existing flakes; signature-related SignatureListPage describe block removed cleanly)
- DB sanity: `sys.schemas WHERE name='signature'` → 0 rows; `sys.tables` in signature schema → 0; `sys.security_policies LIKE '%Signature%'` → 0

## Architecture impact

- BE solution: 142→137 csproj (-5)
- Module count: 17→16 backend modules
- API endpoints: -7 (`/api/v1/signatures/*`)
- DB schemas: 22→21 (signature dropped)
- ModuleAssemblies registry: -1 entry

## QĐ 742 impact

- phase-05b retained, scoped EXTERNAL CA integration ONLY
- Banner added to `docs/compliance/qd-742-cap-4/plan-summary.md`
- Memory `qd-742-cap-4-plan-approved.md` D7 amendment

## Cross-refs

- Plan: `plans/260501-1007-delete-signature-module/`
- Memory pivot: `signature-module-removal-pivot-2026-05-01.md`
- QĐ 742 phase-05b context: `docs/compliance/qd-742-cap-4/plan-summary.md`
- Historical pivot journal: `docs/journals/journal-260422-0907-signature-enrollment-pivot.md`

## Lessons

1. Red-team grep sweep across 8 vectors caught 4 wire-up locations not in initial plan scope (`E2ETestDataSeeder.{Permissions,Menus,RolePermissions}.cs`, `Gateway/OpenApiVietnameseTransformer.cs`, `scripts/seed-permissions.sql`, `PermCodes.cs`). Without the pre-delete sweep, BE build would have failed mid-delete with cascading symbol errors.
2. PermCodes drift pattern (recurring per memory `feedback-architecture-tests-in-finalize-gate.md`) caught 1 stale test (`All_Should_Contain_122_Entries` actual=128 pre-removal). Updated to actual count post-removal as part of same atomic commit.
3. SQL Server schema-local `__EFMigrationsHistory` (`signature.__EFMigrationsHistory`) automatically dropped with `DROP SCHEMA` — no separate `DELETE FROM dbo.__EFMigrationsHistory` needed (modular monolith pattern).
4. FE `forms.signature.clear` i18n preservation explicitly guarded — form widget concern is independent of module removal.
