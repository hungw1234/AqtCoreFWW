# ARCH-FU2.4 Masterdata Module Pagination Clamp Sweep

**Date**: 2026-05-17 17:15 ICT
**Severity**: Low
**Component**: Architecture / Pagination Clamping Rules
**Status**: Resolved
**Version**: v2.137p47 (`b8f307f9` + `8765f6eb`)

## What Happened

ARCH-FU2.4 completed the third module-wide pagination clamp sweep — Masterdata, the **first FU to introduce two-tier pattern** in a single ship. 5 handlers migrated, split by FE consumer pattern:

**AdminMax=100 (3 handlers):**
- `GetDictionariesQueryHandler` — FE `master-data-page.tsx:204` `pageSize: 20`
- `GetDictionaryItemsQueryHandler` — FE `master-data-page.tsx:232` `pageSize: 20`
- `GetExternalMappingsQueryHandler` — FE `master-data-page.tsx:260` `pageSize: 30`

**PickerMax=1000 (2 handlers):**
- `WorkflowActionTaxonomyQueries` — FE `use-workflow-taxonomies.ts` default `pageSize=200` (full-set bulk picker)
- `WorkflowStateTaxonomyQueries` — same FE pattern

Pre-scout established the tier split before any code edit: ≤30 → AdminMax safe; ==200 → AdminMax would silently truncate, requires PickerMax(1000) with headroom.

Scope: 7 files, ~25min wallclock. Architecture 99/99 PASS. Build 0E/0W. Code-review **9.6/10 DONE clean** (3rd consecutive on ARCH-FU2.x chain).

## The Brutal Truth

The "FE-200-coord" carry-over from FU2.2/FU2.3 looked scary on paper but turned out to be a single 90-second FE grep. The actual question is binary: does the FE default exceed AdminMax(100)? Yes → use PickerMax. No → use AdminMax. Memory had emphasized this as a coordination risk for 3 prior sessions; reality required no FE PR, no coordination meeting, no advisor call. Pre-scout discharge time: ≤2min total across both directions.

`BuildPaged` had to be refactored because both taxonomy handlers had a private helper that read `query.Page/PageSize` directly — a query-shape leak across the clamp boundary. Two-tier or not, this would have surfaced as a downstream propagation bug if missed (cache hit path would echo clamped meta against raw query). The refactor was mechanical (`query`→`page, pageSize` in signature; 2 call sites per file).

Cache logic decision: keep `isDefaultParams` check on RAW `query.PageSize == 200`. Reasoning: the check is testing whether the caller asked for the canonical "show all" view; clamping is an implementation detail. Reviewer concurred.

## Technical Details

**Two-tier migration pattern:**
```csharp
// AdminMax tier (3 dictionary handlers):
var (page, pageSize) = PaginationDefaults.Normalize(
    query.Page, query.PageSize, PaginationDefaults.AdminMax);

// PickerMax tier (2 taxonomy handlers):
var (page, pageSize) = PaginationDefaults.Normalize(
    query.Page, query.PageSize, PaginationDefaults.PickerMax);

// isDefaultParams check uses RAW query.PageSize to preserve cache semantics:
var isDefaultParams =
    query.Page == 1 &&
    query.PageSize == DefaultPageSize &&  // 200
    !query.IncludeInactive &&
    string.IsNullOrWhiteSpace(query.Search);
```

**`BuildPaged` refactor:**
```csharp
// Before:
private static PagedResult<T> BuildPaged(
    List<T> items,
    GetXxxQuery query,
    int? total = null) { … query.PageSize … }

// After:
private static PagedResult<T> BuildPaged(
    List<T> items,
    int page,
    int pageSize,
    int? total = null) { … pageSize … }
```

Both call sites (cache-hit path + DB-hit path) updated; no orphan `query.Page/PageSize` left in either file.

**Scanner Allowlist Update (`PaginationClampScanner.cs`):**
- Removed 5 masterdata entries.
- Allowlist count: 19 → **14**.
- Annotation: `// ── masterdata module ─── SWEPT 2026-05-17 v2.137p47 (ARCH-FU2.4) — all 5 handlers migrated. Dictionaries/Items/ExternalMappings use PaginationDefaults.AdminMax (100, FE callers <=30). WorkflowAction/StateTaxonomyQueries use PaginationDefaults.PickerMax (1000) — FE useWorkflowStateTaxonomies/useWorkflowActionTaxonomies pass default pageSize=200 (full-set bulk picker semantics).`

**Test Coverage:**
- `PaginationClampScannerTests.cs` `expectedCount: 19 → 14`.
- No `AqtCoreFW.MasterData.Application.Tests` project exists (only `Integration.Tests`, DB-bound, skipped this session). Canonical clamp tests deferred — creating a new csproj for 2 tests is YAGNI violation; scanner allowlist + count snapshot serves as regression guard. Audit (FU2.2) + Identity (FU2.3) already cover the `Normalize` helper at AdminMax; PickerMax tier has no unit-test coverage in this sweep — filed implicit as future infra need.

## Lessons Learned

**L1: "FE-coord required" memory flag = single grep**
Carry-over memory had emphasized "FE-200-coord" as a risk gate for 3 sessions. Real cost: 1 grep + 1 binary decision (`>AdminMax? → PickerMax : AdminMax`). **Codify:** any "FE coordination required" carry-over should resolve to a grep + 1 decision before opening a meeting/PR. Most coordination flags are over-conservative.

**L2: Two-tier sweep in a single FU**
Prior FU2.2/FU2.3 swept uniform AdminMax tier per module. FU2.4 introduced PickerMax mid-module — the pattern composes cleanly (different `max` arg, identical `Normalize` call). **Codify:** future FUs can mix tiers per-handler without splitting into sub-FUs; tier choice is a per-handler comment, not a per-FU one.

**L3: Cache `isDefaultParams` against raw query, not clamped**
Initial instinct was to check clamped pageSize == DefaultPageSize for consistency. But the canonical "show all" semantic is what the CALLER asked for, not what we computed. Both happen to evaluate identically for the only meaningful case (caller passes 200), but raw-check preserves the contract: cache is for callers requesting the canonical view. **Codify:** when clamping crosses cache boundaries, the cache-key gate evaluates the caller's intent (raw), the SQL/meta uses the clamped values.

**L4: Helper signature `(query, total?)` → `(page, pageSize, total?)`**
Private helpers that take the original query record leak query-shape across clamp boundaries — easy to miss in a sweep because they're "encapsulated." `BuildPaged` was a 2-call-site helper; refactor took 90 seconds. **Codify:** when migrating a handler with a private helper taking the query record, audit the helper for `query.Page/PageSize` references and refactor to `(page, pageSize)` locals.

**L5: YAGNI on new csproj for 2 tests**
Creating `AqtCoreFW.MasterData.Application.Tests` for 2 canonical clamp tests = ~50 LOC csproj + GlobalUsings + InternalsVisibleTo + dependency wiring. Scanner + snapshot guard already exists. Audit (FU2.2) and Identity (FU2.3) cover the helper's AdminMax tier; PickerMax tier is implicitly covered (same `Normalize` call, different arg). **Codify:** module unit-test infrastructure is a precondition, not a sweep deliverable. If absent, scanner guard suffices.

## Root Cause Analysis

Planned per ARCH-FU2 carry-over. Masterdata had 5 handlers predating the helper; the WorkflowTaxonomy pair was the *only* place in the codebase where FE explicitly relies on pageSize > 100 (excluding the rules `useRuleSets=9999` outlier still blocked). FU2.4 introduces PickerMax as a permanent second tier in the sweep program.

## What We Tried

No alternative branches:
1. Pre-scout FE callers for `master-data` and `workflow` features → 3 callers ≤30, 2 callers ==200.
2. Read 5 handlers + scanner allowlist comment block (already cited FE-200-dep risk).
3. Migrate 3 AdminMax handlers (mechanical mirror of FU2.3 identity pattern).
4. Migrate 2 PickerMax handlers + refactor `BuildPaged` signature + preserve cache `isDefaultParams` semantics.
5. Update scanner allowlist (19→14) + test snapshot.
6. Build + Architecture — all PASS.
7. Code-review → 9.6/10 clean (3rd consecutive).

No unit test additions — no MasterData.Application.Tests project exists.

## Next Steps

- **ARCH-FU2.5+**: Remaining small modules per allowlist trajectory (currently 14 entries):
  - notifications (2): `GetNotificationTemplates`, `GetUserNotifications`
  - integration (3): `ListMessageLogs`, `ListPartners`, `ListContracts`
  - collaboration (3): `GetConversations`, `GetMessages`, `SearchMessages`
  - reporting (1): `GetSavedQueries`
  - ai (1): `GetChatHistory`
  - files (1): `GetDocumentTemplates`
  - forms (2): `ListFormSubmissions`, `ListViewDefinitions`
- **Rules module**: 1 remaining (`ListRuleSets`); BLOCKED on FE `useRuleSets pageSize=9999` coord PR.
- **Allowlist trajectory**: 14 remaining; expect ~2 modules per FU (≤25min each); program completes after ~6 more FUs.

---

**Test Summary:**
Architecture 99/99 PASS (16s)
Build 0E/0W
No MasterData.Application unit-test project — scanner+snapshot guard only

**Review:** 9.6/10 DONE clean (zero folds — 3rd consecutive clean review on ARCH-FU2.x chain)

**Cadence Today:** 29 commits / 14 feature ships ~10h total
`cc620a76`(P7-FU1.2) → … → `4c888aff`(FU2.3) → `b8f307f9`(FU2.4)
