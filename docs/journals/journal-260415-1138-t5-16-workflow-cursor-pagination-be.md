# Journal — 2026-04-15 T5-16 Phase 1: Workflow Cursor Pagination (BE)

## Session
- **Date:** 2026-04-15
- **Task:** T5-16 Phase 1 — Workflow Instances cursor-paginated list + summary endpoints
- **Plan:** `plans/260415-1138-t5-16-workflow-cursor-pagination/`
- **Duration:** ~0.5d

---

## What Was Done

Implemented BE cursor-paginated list and KPI summary endpoints for workflow instances against 6 verification gates and 13 RT red-team markers.

### New Endpoints
- `GET /api/v1/workflow/instances` — cursor-paginated, `[Authorize(Policy = "WORKFLOW.INSTANCE.VIEW")]`, `[EnableRateLimiting("read-ops")]`
- `GET /api/v1/workflow/instances/summary` — KPI counts, same auth + rate limit

### Architecture Decisions

**Handlers placed in Infrastructure (deviation from phase file):** Phase file placed handlers in Application. EF Core `DbContext` / `ToQueryString()` requires a relational provider unavailable in Application (which has only Dapper). Follows exact Organization module pattern (`OrgUnitQueryHandlers.cs` in `Organization.Infrastructure`). Second `AddMediatR` scan added to `WorkflowInfrastructureModule` to pick up Infrastructure handlers.

**DateTimeOffset throughout:** Phase pseudocode showed `DateTime`, but `WorkflowInstance.StartedAt` is `DateTimeOffset`. Used `DateTimeOffset` in CursorCodec, DTO, and keyset predicate to prevent implicit comparison mismatch.

**Keyset predicate [RT-01]:** `i.Id < lastId` (direct `<`) not `.CompareTo()`. EF Core translates `<` on `Guid` to `[Id] <` in SQL; `CompareTo` cannot be translated. Verified by `ToQueryString()` test asserting `[Id] <` in generated SQL.

**`PagedResult<T>.TotalCount` nullable [RT-07]:** Changed `int` → `int?`. Cursor mode passes `null` (unknown total); offset mode passes count as before. All existing callers pass `int` which implicitly converts — no call-site changes needed.

### Key Implementation Notes
- `CursorCodec`: `internal`, `InternalsVisibleTo` for Infrastructure + both test projects. Format: `base64(utf8("{startedAt:O}|{id}"))`. `TryDecode` — bool-return, 5 failure branches, no throws.
- Peek-next: fetch `pageSize + 1`, trim last if `count > pageSize`, derive `nextCursor` from last kept item.
- Keyset index: raw SQL migration only [RT-02], no `HasIndex`, no `WHERE` clause [RT-06]. `INCLUDE` covers all projected columns → index-only scan.
- Malformed cursor → `BadRequestError("...", "GOV_WORKFLOW_040")` → HTTP 400 (new error type added to SharedKernel).
- `pageSize` clamped with `Math.Clamp(request.PageSize, 1, 100)` [RT-11].
- Summary handler: 4 `CountAsync` calls (active, completedToday, slaBreach denominator, total); `SlaBreachRate` = `null` when no active instances.

---

## Tests Added (16 new)

### CursorCodecTests (9 — Application.Tests)
- Round-trip × 2 (UTC + non-UTC offset)
- 7 failure paths: null, empty, malformed base64, no separator, bad date, bad guid, whitespace

### ListWorkflowInstancesQueryHandlerTests (7 — Infrastructure.Tests)
- RT-01 proof: `Query_WithCursor_SqlContainsIdLessThan_NotCompareTo` — asserts `[Id] <` in `ToQueryString()`
- RT-11 clamp: 10000 → 100, 0 → 1
- RT-03 bad cursor → `BadRequestError` with correct code
- RT-07 `TotalCount` is `null`
- Peek: N+1 fetch drops last item when `hasNext`
- Peek: exact N returned without `nextCursor` when at end
- Filter: `entityType` filter propagates to query

---

## Verification Gates

| Gate | Status |
|------|--------|
| `dotnet build` zero warnings (all workflow + SharedKernel) | PASS |
| Application tests 399/399 | PASS |
| CursorCodecTests covers all 6 failure cases (9 tests) | PASS |
| `ToQueryString()` contains `[Id] <` | PASS |
| Both controller actions have `[Authorize]` + `[EnableRateLimiting("read-ops")]` | PASS |
| `PermCodes.Workflow.Instance.View` exists | PASS |

---

## RT Marker Compliance

| Marker | Status |
|--------|--------|
| RT-01 keyset `i.Id <` | PASS |
| RT-02 raw SQL migration only | PASS |
| RT-03 `TryDecode` bool-return, no throws | PASS |
| RT-06 no WHERE tautology in migration | PASS |
| RT-07 `TotalCount = null` in cursor mode | PASS |
| RT-11 `Math.Clamp(pageSize, 1, 100)` | PASS |

---

## Concerns

**Double `AddMediatR` registration:** `WorkflowApplicationModule` + `WorkflowInfrastructureModule` each call `AddMediatR`. MediatR 12+ merges registrations without conflict. Integration test validation in Phase 3 will confirm runtime resolution. Same pattern works in Organization module.

---

## Deferred

- Phase 2: FE orval regen + `useWorkflowInstancesInfinite` hook + dashboard wiring
- Phase 3: Integration + Playwright E2E + docs/backlog final sync
- Integration test confirming double `AddMediatR` resolves Infrastructure handlers at runtime
