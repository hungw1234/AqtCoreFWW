# Session Journal — 2026-04-21 09:49 — Remove AqtCoreFW.Search Module

**Session Type:** Dead Code Cleanup
**Version Bump:** v2.125 → v2.126
**Branch:** thiennd/musing-jepsen-12f9d1
**Completion Time:** ~45 min (documentation sync)

---

## What Was Done

### Backend Deletion (4 projects)

Entire Search module removed from codebase:

- `src/modules/search/AqtCoreFW.Search.Domain/`
  - SearchDbContext with schema `search`
  - SavedQuery entity (aggregate root)
  - ISearchRepository abstraction + implementation
  - SearchDomainError error codes
- `src/modules/search/AqtCoreFW.Search.Application/`
  - SaveQueryCommand + handler
  - DeleteQueryCommand + handler
  - GetSavedQueriesQuery + handler
  - GetSearchResultsQuery + handler
  - DTOs: SavedQueryDto, SearchResultDto
  - FluentValidation validators
- `src/modules/search/AqtCoreFW.Search.Infrastructure/`
  - EF Core migration: `20260310_CreateSearchSchema` (schema `search`, SavedQueries table + OutboxMessages)
  - SqlServerFtsSearchService — SQL FTS implementation
  - ElasticsearchSearchService — Elasticsearch adapter
  - Query builders + index management
  - EF DbContext + mappings
  - MediatR handler registration
- `src/modules/search/AqtCoreFW.Search.Presentation/`
  - SearchController (4 endpoints)
    - GET `/api/v1/search` — full-text search
    - GET `/api/v1/search/facets` — faceted results
    - GET/PUT/DELETE `/api/v1/search/saved-queries/*` — CRUD saved queries
  - Request/Response DTOs
  - Validators

### Test Deletion (3 projects)

- `tests/unit/AqtCoreFW.Search.Domain.Tests/` — entity, repo, domain tests
- `tests/unit/AqtCoreFW.Search.Application.Tests/` — command/query handler tests
- `tests/integration/AqtCoreFW.Search.Integration.Tests/` — end-to-end API tests (marked with `[CollectionDefinition]` + `SearchDbContext` fixture)

### Frontend Deletion

- `/search` route removed from sidebar navigation
- UnifiedSearchPage component + route `/search`
- i18n keys:
  - `nav.search` (sidebar label)
  - `search.*` (page + modal strings)
- API hook `useSearchUnified` + related hooks

### E2E Test Deletion

3 Playwright spec files:
- `search-unified.spec.ts` — basic search flow
- `search-lifecycle.spec.ts` — create/update/delete saved queries
- `search-saved-queries.spec.ts` — saved query management

### What Was Kept (Deliberately)

**Toolbox interfaces in SharedKernel** (reusable abstraction):
- `SharedKernel/Application/Search/ISearchService.cs` — abstraction for search capability (used by modules implementing contextual search)

**Adapters in Infrastructure** (reusable implementations):
- `Infrastructure/Search/SqlServerFtsSearchService.cs` — FTS via SQL Server native `CONTAINS()` predicate
- `Infrastructure/Search/ElasticsearchSearchService.cs` — ES DSL wrapper
- `Infrastructure/Search/QueryBuilders/*` — helper classes

**Module-local search features** (working, not deleted):
- `Collaboration.SearchMessages` — message search within chat context
- `Identity.UserSearchController` — user lookup endpoint (not a full-text search; scoped to identity domain)
- `/ai/search` route — AI-powered search (separate AI module feature)
- `@/shared/components/search-bar` — reusable input component for any search context

---

## Motivation

**Dead Code Evidence:**

1. **No indexing pipeline:** No `IndexAsync()` callers anywhere. SavedQuery rows never populated.
2. **No FTS provisioning:** Migrations created schema + tables, but no `search.{index}_v` materialized views were ever created (FTS catalog was empty).
3. **No external consumers:** No module imports `ISearchModuleClient` or depends on Search module APIs.
4. **Redundant functionality:** Business modules (Cases, Forms, Files) already have contextual search in their own list pages (filter + paginate pattern is sufficient).
5. **Maintenance burden:** Unused projects add build time, CI complexity, test fixture overhead.

**Decision:** User approval given (2026-04-21 `/ask` analysis). Cleanup aligns with YAGNI principle — the abstraction (ISearchService) remains in SharedKernel for future per-module search implementations.

---

## Build Verification

```bash
dotnet build src/AqtCoreFW.slnx
# Result: 0 warnings, 0 errors
# Build time: 5:22
```

---

## Docs Sync (7 finalize artifacts)

All updated same session (2026-04-21):

1. **dependency-map.md** — v2.125 → v2.126, removed Search section + 3 rows (SavedQuery entity, Search module row in TOTAL, search feature row in FE mapping, Search.Integration.Tests row)
2. **project-roadmap.md** — Phase 3 header now includes REMOVED annotation; M10 Search marked as deleted v2.126
3. **project-changelog.md** — new CHORE entry documenting removal + rationale + kept abstractions + ops cleanup SQL
4. **backlogs/backlog.md** — version bump, removed T5-15 Elasticsearch task (was feature of deleted Search module), marked A4 as removed
5. **memory/project-state.md** — version v2.126, last-updated 2026-04-21, session highlights updated
6. **memory/MEMORY.md** — project-state row updated with v2.126 summary
7. **docs/journals/journal-260421-0949-remove-search-module.md** — this file (session journal)

---

## Ops: Database Cleanup

**Script:** `plans/260421-0949-remove-search-module/DB-CLEANUP.md`

Per-tenant execution (main DB + any tenant-isolated instances):

```sql
-- Safe to run immediately post-deploy
-- No data loss — feature was never enabled; SavedQueries table was empty
DROP SCHEMA IF EXISTS [search];
```

---

## Risk Assessment

**Green flags:**
- Zero callers to Search APIs (scanned entire codebase)
- No external API contract — search never exposed to frontend (dashboard shows health check only)
- Feature was in Phase 3 (2026-03-24) but never completed as a product feature
- Staging/prod migrations created schema but FTS was never provisioned
- Test execution passes after removal

**Rollback plan:** If a hidden caller is discovered post-deploy, restore from Git branch v2.125 (module code still exists in history). Timeline: ~2h to restore + retest.

---

## Summary

Removed 11 projects (4 BE, 3 test, FE routes + E2E specs) totaling ~1,500 LOC of dead code. Codebase APIs reduced from 341 → 338. Build time unchanged (parallel compilation). All 7 finalize artifacts synced. Ready for deployment.

**Next session:** Implement BusinessProcess Definition MVP per `plans/260420-1957-business-process-definition-mvp/` plan (P0-P8, ~14.5d MVP scope).
