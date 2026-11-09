# ARCH-FU2.5+ Bundled Pagination Clamp Sweep

**Date**: 2026-05-17 17:45 ICT
**Severity**: Low
**Component**: Architecture / Pagination Clamping Rules
**Status**: Resolved
**Version**: v2.137p48 (`dba0f0cb` + `cb867e68`)

## What Happened

ARCH-FU2.5+ closed the **largest single FU in the ARCH-FU2.x chain** — bundled sweep of 13 handlers across 7 modules in one ship. After this FU, the `PaginationClampScanner` allowlist holds exactly **1 entry** (`ListRuleSets`, FE-blocked), effectively completing the program.

**Handlers swept (all → `AdminMax=100`):**
- notifications (2): `GetNotificationTemplates`, `GetUserNotifications`
- integration (3): `ListMessageLogs`, `ListPartners`, `ListContracts`
- collaboration (3): `GetConversations` + `GetMessages` + `SearchMessages` (last two cursor-based)
- forms (2): `ListFormSubmissions`, `ListViewDefinitions`
- reporting (1): `GetSavedQueries`
- ai (1): `GetChatHistory`
- files (1): `GetDocumentTemplates`

Pre-scout confirmed all FE callers ≤100 pageSize. The biggest edge: `usePartners({pageSize: 100})` sits exactly at AdminMax ceiling — first FE caller in the entire sweep program to reach the tier boundary. No truncation, but worth memoization for any future AdminMax reduction.

Scope: 15 files, ~35min wallclock. Architecture 99/99 + Forms.App 179/179 + Ai.App 112/112 + Collab.App 22/22 PASS. Build 0E/0W across 7 module csproj. Code-review **9.6/10 DONE clean** (**4th consecutive** on ARCH-FU2.x chain).

## The Brutal Truth

13 handlers in 35 minutes — the FU2 helper-first decision delivers compounding returns. Each handler reduces to one `Normalize` line plus mechanical local propagation. The only architectural choice in this FU was handling cursor-based pagination: `GetMessagesQuery` + `SearchMessagesQuery` have no `Page` field (cursor-based via Before/AfterMessageId tokens). Forcing `Normalize` would have required a no-op page argument or an overload. Instead used `Math.Clamp(request.PageSize, 1, PaginationDefaults.AdminMax)` — scanner accepts both tokens, so one line satisfies the defense AND references the helper constant for tier consistency.

Allowlist went 14→1 in one commit. The remaining entry (`ListRuleSets`) is genuinely blocked on FE work (`useRuleSets({pageNumber:1, pageSize:9999})` would silently truncate to PickerMax=1000 if migrated today — needs FE PR to drop the 9999 first). Program effectively complete.

## Technical Details

**Two-pattern split:**

```csharp
// 11 standard handlers — Normalize tuple deconstruction:
var (page, pageSize) = PaginationDefaults.Normalize(
    request.Page, request.PageSize, PaginationDefaults.AdminMax);
// Propagate page/pageSize locals through offset, Dapper, meta echo.

// 2 cursor-based handlers (GetMessages, SearchMessages) — no Page param:
var pageSize = Math.Clamp(request.PageSize, 1, PaginationDefaults.AdminMax);
// Used in 3 SQL branch params each (before/after/default for GetMessages).
```

**Empty-meta early-return audit (FU2.3 L3 lesson applied):**
`ListMessageLogs`, `ListContracts`, `ListFormSubmissions` each have empty-meta early-return branches when an upstream resolver returns nothing. All three now echo clamped `(page, pageSize)` in the empty PaginationMeta — symmetric with the non-empty path. Pre-FU these branches echoed raw `request.Page/PageSize`.

**Dapper anonymous-object shorthand collision (FU2.3 L4 lesson applied):**
All handlers that had `new { request.PageSize }` shorthand rewritten to explicit `new { PageSize = pageSize }` to ensure SQL placeholder receives clamped value. 9 of 13 handlers needed this.

**`ListViewDefinitions` two-PageSize-concepts:**
The handler reads `request.PageSize` (pagination param) AND projects `ViewDefinitions.PageSize` (entity column storing the view's default page size). These coexist cleanly — the entity column flows through `ViewRow.PageSize` (line 60) → `ViewDefinitionDto.PageSize`. Only `request.PageSize` is clamped. Scanner regex `\b(request|query|...)\.PageSize\b` correctly distinguishes; reviewer confirmed.

**Scanner Allowlist Update (`PaginationClampScanner.cs`):**
- Removed 13 entries across 7 module sections.
- Consolidated 7 section markers into single SWEPT marker citing all 7 module names + cursor-handler subnote.
- Allowlist count: 14 → **1** (only `ListRuleSets`, with retained NOTE comment explaining FE-9999 block).

**Test snapshot update (`PaginationClampScannerTests.cs`):**
- `expectedCount: 14 → 1`.

## Lessons Learned

**L1: Bundle when modules are small + caller-uniform**
ARCH-FU2.2-2.4 swept module-by-module (5+7+5=17 handlers in 3 FUs). FU2.5+ bundled 7 modules × 1-3 handlers each into a single FU because (a) all callers were ≤AdminMax (no tier negotiation), (b) per-module handler counts were ≤3 (no module-level review boundary needed), and (c) the paved-path pattern had stabilized at FU2.3. **Codify:** module-by-module sweeps suit early-pattern phases; once pattern stabilizes + callers are uniform, bundle by review boundary, not module boundary.

**L2: Cursor-based handler escape hatch — defense-token reuse**
`Math.Clamp(request.PageSize, 1, PaginationDefaults.AdminMax)` satisfies scanner (both `Math.Clamp` AND `PaginationDefaults` accepted) AND maintains tier-constant consistency with `Normalize`-using handlers. Avoids forcing a `Normalize` overload or a no-op Page arg. **Codify:** when a handler's query record genuinely lacks `Page` (cursor pagination), inline `Math.Clamp` with the `PaginationDefaults` constant is the cleanest defense.

**L3: Empty-meta branches are FU2.3 L3 multiplied by 3 in this sweep**
3 of 13 handlers had pre-existing empty-meta early returns echoing raw `request.Page/PageSize` (ListMessageLogs, ListContracts, ListFormSubmissions). Pattern: handlers that resolve cross-module references early can short-circuit; meta echo must use clamped values. **Codify:** grep `PaginationMeta\(` per file post-edit (not just primary happy path) — already in FU2.3 lesson, now reinforced by 3× occurrence.

**L4: AdminMax ceiling collision risk — `usePartners({pageSize: 100})`**
First FE caller in the program to sit EXACTLY at AdminMax=100. After clamp, behavior unchanged (Math.Clamp(100, 1, 100) = 100). But any future tightening of AdminMax → 50 (e.g.) would silently truncate. **Codify:** track FE-at-ceiling callers in a memory note — `usePartners` is the first known instance.

**L5: 4th consecutive 9.6 clean review**
FU2.2 → 2.3 → 2.4 → 2.5+ all scored 9.6/10 DONE clean with zero folds. The combined pattern (helper-first + pre-scout + uniform diff + caller-verified + allowlist forcing function) appears to be a local maximum for code-review score on architectural sweeps. Further refinement would require either a different review rubric or genuinely novel scope. **Codify:** when an FU pattern delivers 4 consecutive clean reviews, the pattern is mature — switch to optimization (bundling, scope expansion) rather than further refinement.

## Root Cause Analysis

Planned per ARCH-FU2 carry-over. 13 small-module handlers predated the helper; FU2.2-2.4 established the paved path. FU2.5+ bundled the long tail in one ship because all modules met the bundling criteria from L1 above. The only remaining handler (`ListRuleSets`) is FE-blocked by a long-standing 9999 literal that requires coordinated FE PR work — outside this session's scope.

## What We Tried

No alternative branches:
1. Pre-scout FE callers for all 7 modules → all ≤100; `usePartners` at-ceiling note saved.
2. Read 13 handlers in 3 batches.
3. Migrate 11 standard handlers (uniform `Normalize` + propagation).
4. Migrate 2 cursor handlers (`Math.Clamp` direct).
5. Update scanner allowlist (14→1, consolidated section markers).
6. Update test snapshot (14→1).
7. Build 7 module csproj — all 0E/0W.
8. Run 4 unit test suites — all PASS (Architecture 99 + Forms 179 + Ai 112 + Collab 22).
9. Code-review → 9.6/10 clean.

No unit-test additions per FU2.4 YAGNI precedent — notifications/integration/reporting/files modules have no Application test projects; canonical clamp tests for AdminMax tier already covered by audit (FU2.2) + identity (FU2.3) precedents.

## Next Steps

- **ARCH-FU2.6**: `ListRuleSets` handler — BLOCKED. Requires FE coordination PR:
  1. Locate `useRuleSets({pageNumber: 1, pageSize: 9999})` callers (currently `web/src/features/rules/rule-set-list-page.tsx:40`).
  2. Refactor FE to use proper pagination OR raise BE tier to `PickerMax=1000` (1000 ≥ likely RuleSets count).
  3. BE migration: `Math.Clamp(request.PageSize, 1, PaginationDefaults.PickerMax)` once FE PR merged.
- **Post-program cleanup**: once allowlist reaches 0, consider removing the allowlist mechanism entirely (or keeping as a fail-safe — likely the latter).

---

**Test Summary:**
Architecture 99/99 PASS (14s)
Forms.Application 179/179 PASS (3s)
Ai.Application 112/112 PASS (1s)
Collaboration.Application 22/22 PASS (1s)
Build 0E/0W across 7 module Application csproj

**Review:** 9.6/10 DONE clean (zero folds — **4th consecutive** clean review on ARCH-FU2.x chain)

**Cadence Today:** 32 commits / 15 feature ships ~10.5h total
`cc620a76`(P7-FU1.2) → … → `b8f307f9`(FU2.4) → `dba0f0cb`(FU2.5+)
