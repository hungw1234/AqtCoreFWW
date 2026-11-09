# ARCH-FU3 — Direct-`Math.Clamp` Audit & Normalize Migration

**Date**: 2026-05-17 18:50 ICT
**Severity**: Low
**Component**: Architecture / Pagination defense-token consistency
**Status**: Resolved — SHIPPED
**Version**: v2.137p50

## What Happened

Post-ARCH-FU2.6 (program-complete) follow-up surfaced during `/aqt-next` orientation: while the scanner accepts both `Math.Clamp(_, 1, X)` and `PaginationDefaults.Normalize(...)` as defense tokens, the codebase had drifted into two idioms for the same operation. ARCH-FU3 audits all 13 `Math.Clamp(_, _, PageSize)` sites and migrates the 7 that should canonicalize onto `PaginationDefaults.Normalize` / `PaginationDefaults.AdminMax`, while explicitly justifying the 6 that should not.

Scope: 7 file edits, 0 behavior change, ~25min wallclock. Review **9.7/10 DONE clean** (6th consecutive ≥9.5 on ARCH chain).

## Decision Matrix

| Site | Action | Reason |
|------|--------|--------|
| `ListSodViolationLogsQueryHandler:23` | Migrate to `Normalize(AdminMax)` | Page+PageSize handler, tier=100 |
| `GetWorkflowDefinitionsQueryHandler:21` | Migrate to `Normalize(PickerMax)` | Page+PageSize, picker-tier=1000 (FE `useWorkflowDefinitionPicker` 500) |
| `ListDelegationsQueryHandler:51` | Migrate to `Normalize(AdminMax)` | Page+PageSize, tier=100 |
| `ListBusinessProcessesQueryHandler:71` | Migrate to `Normalize(AdminMax)` | Page+PageSize, tier=100; cache key (`TryGetActiveCacheKey`) untouched — checks RAW `request.PageSize == 100` (caller intent) |
| `GetFormTemplatesQueryHandler:22` | Migrate to `Normalize(PickerMax)` | Page+PageSize, picker-tier=1000 (FE `useFormTemplatePicker` 500) |
| `ListFilesQueryHandler:22` | Migrate to `Normalize(AdminMax)` | Page+PageSize, tier=100 |
| `WorkflowInstanceQueryHandlers:30` | Literal `100` → `PaginationDefaults.AdminMax` | Cursor-only handler (no Page param); cannot use `Normalize`. Mirrors Collaboration cursor pattern. |
| `GetMigrationItemsQueryHandler:24` | **Keep** | Custom cap=200 (not a tier) + FluentValidation `InclusiveBetween(1,200)` — doubly defended; YAGNI on new tier |
| `SearchUsersQueryHandler:18` | **Keep** | No Page param — autocomplete using `TOP @PageSize` + cap=50, semantically not pagination |
| `SqlServerFtsSearchService:41` | **Keep** | Shared search infra — scanner targets `*QueryHandler`, this is `*SearchService` |
| `ElasticsearchSearchService:47` | **Keep** | Same |
| `GetMessagesQueryHandler:73` | **Already correct** | Already uses `PaginationDefaults.AdminMax` |
| `SearchMessagesQueryHandler:45` | **Already correct** | Already uses `PaginationDefaults.AdminMax` |

## The Brutal Truth

This wasn't a forcing-function failure — the scanner would have accepted every site as-is. ARCH-FU3 is cosmetic by strict YAGNI: zero behavior change, zero bug closed. The value is non-functional but real: a future reader sees one idiom for "clamp PageSize" instead of having to mentally parse "is this `Math.Clamp` form equivalent to that `Normalize` form, or did someone make a different defensive choice here?". That's the kind of reading-tax that compounds across a 31-handler-paginated codebase.

The first decision burned a minute: should `WorkflowInstanceQueryHandlers` get a `Normalize` call too? No — `Normalize` returns `(Page, PageSize)` and the cursor-paginator has no Page semantic. The right move was to constify the literal `100` to `PaginationDefaults.AdminMax` and leave the `Math.Clamp` skeleton. This matches what FU2.5+ established for `GetMessages`/`SearchMessages`. Two idioms still exist in the codebase — `Normalize` for Page+PageSize, `Math.Clamp + tier const` for cursor — but they encode a real semantic distinction, not an arbitrary one.

## Technical Details

**Pattern A — Page+PageSize handlers (6 sites):**

```csharp
// Before
private const int MaxPageSize = 1000;
...
var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
var page = Math.Max(1, request.Page);

// After
var (page, pageSize) = PaginationDefaults.Normalize(
    request.Page, request.PageSize, PaginationDefaults.PickerMax);
```

Local `MaxPageSize` consts removed (3 of 6 had them). `ListSodViolationLogsQueryHandler` also dropped an unused `DefaultPageSize=50`. Two files needed a new `using AqtCoreFW.SharedKernel.Application.Pagination;`.

**Pattern B — Cursor handler (1 site):**

```csharp
// Before — WorkflowInstanceQueryHandlers.cs:30
var pageSize = Math.Clamp(request.PageSize, 1, 100);

// After
// Cursor-based pagination (no Page param) → Math.Clamp + PaginationDefaults.AdminMax
// is the canonical idiom (mirrors Collaboration.GetMessages / SearchMessages).
var pageSize = Math.Clamp(request.PageSize, 1, PaginationDefaults.AdminMax);
```

XML doc updated: `[RT-11] PageSize clamped server-side to [1, 100]` → `[1, PaginationDefaults.AdminMax]`.

**Cache-key non-regression:** `ListBusinessProcessesQueryHandler.TryGetActiveCacheKey` checks `request.PageSize == 100` on the RAW request (caller intent). Migrating the SQL-path local to `Normalize` does not touch the cache key — semantics preserved exactly.

**Validation:**
- 8 csprojs build 0E/0W (7 module .csproj + Architecture.Tests).
- Architecture: 99/99 PASS (scanner allowlist remains 0; defense tokens still recognized).
- Identity.App 245/245, Workflow.App 433/433, Workflow.Infra 58/58, BP.App 88/88, Forms.App 179/179, Files.App 112/112 — 1215 unit tests, 0 failures.
- Code-review: **9.7/10** DONE clean — verified tier preservation, cache-key semantics, no Dapper anon-obj shorthand collisions, no public-signature changes.

## Lessons Learned

**L1: Post-program audits have a different rubric.** ARCH-FU2.x shipped a forcing function (scanner + allowlist). The program ended at allowlist=0. But "allowlist=0" doesn't mean "all sites are idiom-consistent" — it means "all sites pass at least one defense token." A post-program audit looks at idiom convergence, which is cosmetic-but-real. Codify: when a forcing-function program completes, run one cosmetic audit pass to canonicalize before declaring done.

**L2: Cursor vs Page+PageSize is a real semantic axis, not an arbitrary one.** Tempting to push every clamp through `Normalize` for uniformity, but `Normalize` returns a `(Page, PageSize)` tuple — its existence implies an `OFFSET (page-1)*pageSize` SQL shape. Cursor paginators have no Page; forcing them through `Normalize` would either discard the Page output (wasteful) or imply something untrue. Two idioms is correct here; the distinction is the semantic.

**L3: 5/13 "skip" justifications are as valuable as the 7 migrations.** The audit doc captures *why* `GetMigrationItemsQueryHandler` (custom cap, doubly defended), `SearchUsersQueryHandler` (autocomplete TOP), `SearchService` files (out of handler scope), and the two cursor handlers (already correct) are intentionally left in their current form. Without this record, the next contributor will re-audit them, or worse, "normalize" them and introduce a subtle bug. Codify: every audit doc should enumerate skips with reasons, not just migrations.

**L4: 6 consecutive ≥9.5/10 on ARCH chain.** FU2.2→FU2.6 were 5×9.6; FU3 is 9.7. Pattern is well past terminal velocity for this rubric. Next ARCH work should either tackle novel scope (cross-program ports) or change the rubric (security-tier reviews).

## What We Tried

1. `/aqt-next` orient → backlog scan returned "no ready rows, all trigger-gated"; recommended `/aqt-stop`.
2. User invoked `/cook Audit Math.Clamp --auto` instead.
3. Grep `Math\.Clamp\s*\([^,)]*PageSize` over `src/` → 13 sites.
4. Per-site read → categorize (Page+PageSize vs cursor vs autocomplete vs infra vs already-correct).
5. Apply 7 edits.
6. Build 8 csprojs → 0E/0W.
7. Run arch + 6 module test suites → all green.
8. Code-reviewer subagent → 9.7/10 clean, auto-approve under --auto.
9. Backlog row + this journal + 3-commit trio + push.

## Next Steps

ARCH-FU3 is shipped end-to-end. No carry-over.

Optional future audits (NOT planned):
- Audit `Math.Max(1, _.Page)` standalone sites (without companion clamp on PageSize) — would catch handlers that have a Page but lack a PageSize control.
- Audit non-PageSize-related `Math.Clamp` sites for similar tier-naming opportunities (e.g., audit window sizes, retention durations).

---

**Test Summary:**
Architecture 99/99 PASS
Identity.App 245/245, Workflow.App 433/433, Workflow.Infra 58/58, BP.App 88/88, Forms.App 179/179, Files.App 112/112
8 csprojs build 0E/0W

**Review:** 9.7/10 DONE clean (zero blockers, zero folds — **6th consecutive ≥9.5** on ARCH-FU chain)

**Cadence Today:** 38 commits / 17 feature ships ~12h total (last: `0bc07980` FU2.6 PROGRAM COMPLETE → this FU)
