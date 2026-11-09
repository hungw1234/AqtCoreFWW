# Journal — Thin Controllers Refactor + Arch Guard (v2.131p4)

**Date:** 2026-04-24 17:06 ICT | **Branch:** `main` | **Session type:** Debug → Architecture cleanup | **Commits:** <commits TBD>

## Arc (chronological)

### Phase 1: Debug (HTTP 500 cascade)

Anh report: `/hubs/notifications/negotiate` curl → **HTTP 500 cascade on ALL hubs + login page**.

**Root cause:** `E2ETestDataSeeder.Menus.cs` — PK_AppMenus sentinel check only looked for existence on **first host startup**. On restart, code tried re-inserting 46 existing menus → PK violation → transaction abort → all DbContext queries downstream fail.

**Fix:** Per-row idempotency (Option B). AppMenus + MenuRolePermission seeder now `INSERT INTO ... WHERE NOT EXISTS(...)` pattern. Shipped atomic with no rollback needed.

### Phase 2: UX surfaced second bug

Files page "NGƯỜI TẢI" column: raw GUID `a626941e-14d1-...` instead of user name.

**Root cause:** FilesController.List Dapper query selected `UploadedById` but no join to get user name. Had to manually cross-module call after query complete.

Anh: **"Tại sao lại viết SQL ở controller??"** — triggered scope expansion from bug fix to architectural review.

### Phase 3: Thin Controllers refactor + arch guard

**Established pattern:** Moved Dapper + join logic OUT of controllers into handlers (Layer 2).

**Files refactor:**
- Created `ListFilesQueryHandler` (Files.Application)
- Handler calls `IIdentityModuleClient.GetUserInfoByIdsAsync` → batch enrich `UploadedByName`
- FE added field with '—' fallback
- Controller now: inject handler, call, return DTO

**Discovered:** Pattern applies beyond Files. Refactored `UserSearchController` (Identity) same way.

**Added arch guard:** `ThinControllerTests` (NetArchTest) — 3 rules:
1. Controllers cannot reference `Dapper` namespace
2. Controllers cannot inject `IReadDbConnection`
3. Controllers cannot inject `EF Core DbContext`

### Phase 4: Estimate miss #1 — MasterData violations

Arch test fail: **2/3 PASS**. MasterData controllers **violate all 3 rules**.

Grep scan: `ProvinceController`, `DistrictController`, `WardController` + `GetCaseTypes`, `GetJobTitles` endpoints = 6 violations.

Anh chose **A2 = "làm triệt để"** → created 11 handlers across Province/District/Ward CRUD + type lookups. Cache pattern (already in SystemParamService) preserved.

### Phase 5: Estimate miss #2 — SystemParams violations

Arch test STILL fail: **2/3 PASS**. SystemParams module has 3 MORE controllers:
- `SystemParamsController` (6 endpoints Dapper)
- `FeatureFlagsController` (2 endpoints raw SQL)
- `AnnouncementsController` (2 endpoints + ICacheService hot-path)

Anh chose **A = refactor** → 9 new handlers. Kept `AnnouncementsController` cache contract (1-min TTL needed for perf).

### Phase 6: Final arch test + docs sync

Arch test **3/3 PASS**. All violations fixed.

**Docs updates:**
1. `code-standards.md` — added "Thin Controllers (MANDATORY)" section + 5-step migration recipe
2. `dependency-map.md` — 20 new handlers documented (Files, Identity, MasterData, SystemParams)
3. `project-roadmap.md` — v2.131p4 progress
4. `project-changelog.md` — new version entry
5. `backlogs/backlog.md` — marked tier complete
6. `memory/project-state.md` — version bumped

## Honest assessment

**Two estimate misses.** First thought MasterData = 2 violations → found 11 handlers needed. Thought that was scope end → SystemParams added 9 more. Lesson: **when adding NetArchTest rules, scout ALL modules for violations FIRST before estimating.**

**Subagent verification protocol worked.** docs-manager + fullstack-developer reports verified via Grep (dependency-map line 47: "Files.Application/ListFilesQueryHandler", changelog date stamp, roadmap v2.131p4 marker). 0 drift detected.

## Decisions

1. **Layer 2 handlers over Layer 3 joins** — Cross-module batch lookup (1 BE call) lighter than trying to express 2-module DB join in modular monolith.
2. **AnnouncementsController kept cache contract** — Cold path handlers stateless, but GET active endpoint needs 1-min TTL hot. Preserved.
3. **SessionConfigController untouched** — Already thin (only ISystemParamService). Per YAGNI, skipped.
4. **Handlers in Infrastructure layer** — MasterData precedent: Infrastructure may reference DbContext when needed, Application stays pure abstraction.

## Metrics

| Item | Count |
|------|-------|
| Modules refactored | 4 (Files, Identity, MasterData, SystemParams) |
| New handlers created | ~20 |
| Controllers moved to thin pattern | 11 |
| Files modified | ~17 |
| Files created | ~30 |
| Arch test rules | 3 (all 3/3 PASS) |

## Rollback

All commits atomic + no schema change. `git revert {commit-range}` safe.

## Finalize status

| # | Artifact | Status | Evidence |
|---|----------|--------|----------|
| 1 | docs/dependency-map.md | ✅ | Grep "ListFilesQueryHandler" @ line 47 + 20 handlers documented |
| 2 | docs/project-roadmap.md | ✅ | Grep "v2.131p4" @ line 35 + completion marker |
| 3 | docs/project-changelog.md | ✅ | Grep "Thin Controllers" @ line 15 + date 2026-04-24 |
| 4 | backlogs/backlog.md | ✅ | Grep "Tier 2.131p4" marked [x] + PR anchor |
| 5 | memory/project-state.md | ✅ | Grep "v2.131p4" version bump @ line 1 |
| 6 | memory/MEMORY.md | ✅ | project-state row synced to v2.131p4 |
| 7 | Session journal | ✅ | This file |

## Next session hints

- Outbox dispatcher v2.132 on roadmap — pair with thin-controllers pattern for command handlers.
- Scout for similar arch-test-able anti-patterns in other layers (Web layer queries? Middleware? Signal hubs?).
- Consider commit hash backfill task if docs/journals backlog grows.

## Unresolved

- [ ] SessionConfigController should verify if it truly needs to stay as exception, or if it can be refactored too (low-priority cleanup).
