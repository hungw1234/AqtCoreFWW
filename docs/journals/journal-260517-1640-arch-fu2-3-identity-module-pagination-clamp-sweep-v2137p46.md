# ARCH-FU2.3 Identity Module Pagination Clamp Sweep

**Date**: 2026-05-17 16:40 ICT
**Severity**: Low
**Component**: Architecture / Pagination Clamping Rules
**Status**: Resolved
**Version**: v2.137p46 (`4c888aff` + `500ce3e2`)

## What Happened

ARCH-FU2.3 completed the second module-wide pagination clamp sweep — Identity, the largest single slice (7 handlers). All migrated from raw `request.Page` / `query.PageSize` to `PaginationDefaults.Normalize(_, _, AdminMax=100)` tuple deconstruction. Handlers swept: `ListPendingAccessReviews`, `ListActiveSessions`, `ListCredentialPolicies`, `ListExternalIdentities`, `ListJitProviderConfigs`, `ListTenantsAdmin`, `ListUsers`.

FE pre-scout (recommended by ARCH-FU2.2 carry-over) cleared the coordination flag: all identity-feature hooks pass `pageSize <= 100` (largest 100 at `external-identities-page.tsx:45` `useUsers({ pageSize: 100 })` + `jit-provider-config-api.ts:68` `{ pageSize: 100 }`). AdminMax=100 lands exactly on the existing ceiling — zero FE flow broken.

Scope: 10 files, ~30min wallclock. Architecture 99/99 + Identity.Application 243→245/245 PASS. Build 0E/0W. Code-review **9.6/10 DONE clean** (zero folds — 2nd consecutive clean review on ARCH sweep).

## The Brutal Truth

The largest slice was the easiest. 7 handlers, but the FU2.2 paved path collapsed the work to mechanical repetition: read handler → insert `Normalize` at entry → rename `request.PageSize` → `pageSize` → done. Scout (PaginationClampScanner allowlist) handed the file list pre-sorted; advisor not invoked because pre-scout took <90 seconds and removed the only ambiguity. Helper-FIRST forcing function reproven — five-line-clone risk eliminated by helper centralization in ARCH-FU2.

Estimate was 25-30min (per memory carry-over "identity ~1.5-2h" was stale from before FU2.2 paved the pattern). Actual: 30min. Compression near 1x — well-scouted work stops compressing once tooling stabilizes.

## Technical Details

**Migration Pattern (uniform across all 7):**
```csharp
// Handler entry — clamp + deconstruct
var (page, pageSize) = PaginationDefaults.Normalize(
    query.Page, query.PageSize, PaginationDefaults.AdminMax);

// Downstream usage propagates locals:
var offset = (page - 1) * pageSize;
// Dapper param: PageSize = pageSize  (explicit assignment — no shorthand collision)
var totalPages = (int)Math.Ceiling((double)total / pageSize);
var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);
```

Two handler variants required slightly different propagation paths:
- **Dapper-direct (4 handlers):** `ListCredentialPolicies`, `ListExternalIdentities`, `ListJitProviderConfigs`, `ListUsers` — clamp before `offset`, swap anonymous-object `query.PageSize` shorthand → explicit `PageSize = pageSize` to avoid name collision risk.
- **Repository delegate (3 handlers):** `ListPendingAccessReviews`, `ListActiveSessions`, `ListTenantsAdmin` — clamp before repo call so both repo args AND the empty-result early-return PaginationMeta echo use clamped values.

`ListTenantsAdmin` had an early-return empty branch that previously echoed `query.Page/PageSize`; now correctly echoes clamped `page/pageSize`. Code-review L-2 flagged this as a sneaky drift risk caught clean.

**Scanner Allowlist Update (`PaginationClampScanner.cs`):**
- Removed 7 identity entries (both identity blocks collapsed).
- Allowlist count: 26 → **19**.
- Section annotated `// ── identity module ─── SWEPT 2026-05-17 v2.137p46 (ARCH-FU2.3) — all 7 handlers migrated to PaginationDefaults.Normalize(AdminMax=100). FE pre-scout: all identity hooks pass pageSize<=100.`

**Test Coverage:**
- `PaginationClampScannerTests.cs` `expectedCount: 26 → 19`.
- `ListUsersQueryHandlerTests.cs` +2 canonical clamp tests:
  - `Handle_PageSizeAboveAdminMax_ClampsToAdminMax` (9999 → 100)
  - `Handle_PageBelowOne_ClampsToOne` (0 → 1)
- Identity.Application suite: 243 → 245 PASS.

## Lessons Learned

**L1: Paved-path multiplier confirmed**
ARCH-FU2 (helper) + ARCH-FU2.2 (audit precedent) reduced the largest remaining slice to <1x compression in 30 minutes. No design questions, no advisor calls, no review folds. **Codify:** when N handlers follow identical pattern AND a tested precedent module exists, sweep without scout/advisor — just mirror the precedent commit's structure. Pre-scout limited to FE caller pageSize check (<90s).

**L2: Pre-scout discharge protocol**
Memory carry-over flagged "pre-scout req" for identity. Real cost: 1 grep across 6 directories, 1 minute. Output: confirmation that FE max=100 matches AdminMax=100 exactly. **Codify:** "pre-scout req" memory flags should resolve to a single grep+verify step, not a planning phase. If the grep returns ≤chosen-tier, scope is clean.

**L3: Empty-result branch echo trap**
`ListTenantsAdminQueryHandler` empty-list early-return echoed `query.Page/PageSize` rather than clamped values. Easy to miss because the handler "succeeds" with empty data regardless. Reviewer L-2 verified clean. **Codify:** when migrating to clamped pagination, audit ALL meta-echo sites in a file (early returns, success branches, error branches), not just the primary happy path. Grep `PaginationMeta\(` per file post-edit.

**L4: Dapper anonymous-object shorthand collision risk**
`new { query.PageSize }` shorthand expands to `PageSize = query.PageSize`. After clamping, this would echo unclamped value to SQL — silent breakage. Required explicit `PageSize = pageSize` rewrite in 4 handlers. **Codify:** any Dapper anonymous-object using shorthand for a clamp-eligible field MUST be rewritten to explicit assignment during clamp migration. Easy grep: `new \{[^}]*\.PageSize[^=]` (no `=` after).

**L5: 2nd consecutive 9.6/10 clean review on ARCH sweep**
Reviewer verified 4 angles in <2min: (1) all 7 handlers clamp at entry, (2) locals propagated to all downstream sites (SQL + meta echo), (3) allowlist count math (26−7=19) verified, (4) FE pre-scout confirms no caller breaks. Pattern continues: well-scouted + helper + caller-verified + uniform diff = nothing for reviewer to flag.

## Root Cause Analysis

Planned per ARCH-FU2 carry-over. 7 identity handlers predated the helper; ARCH-FU2 deferred per-module sweeps because FE consumers needed verification. ARCH-FU2.2 audit sweep proved the paved path; FU2.3 is direct application of the same template to the next-largest module.

## What We Tried

No alternative branches:
1. Pre-scout FE callers for identity endpoints → all ≤100 pageSize.
2. Read 7 handlers + audit precedent (`GetAuditLogsQueryHandler`).
3. Migrate 7 handlers (parallel edits).
4. Update scanner allowlist (26→19) + test snapshot.
5. Add 2 canonical clamp tests on `ListUsers` (largest endpoint).
6. Build + Architecture + Identity.Application — all PASS.
7. Code-review → 9.6/10 clean.

## Next Steps

- **ARCH-FU2.4**: Masterdata (5 handlers — `GetDictionaries`, `GetDictionaryItems`, `GetExternalMappings`, `WorkflowActionTaxonomyQueries`, `WorkflowStateTaxonomyQueries`). **FE-200-coord required:** `useWorkflowStateTaxonomies` / `useWorkflowActionTaxonomies` rely on BE default ~200 — must use `PickerMax=1000` not `AdminMax=100`, OR coordinate with FE to drop the 200 literal.
- **ARCH-FU2.5-.x**: Notifications (2), Integration (3), Collaboration (3), Reporting (1), AI (1), Files (1). Small slices — likely <25min each.
- **Rules module**: BLOCKED on FE `useRuleSets pageSize=9999` coordination. Requires explicit FE drop-to-1000 PR before BE migration.
- **Allowlist trajectory**: 19 remaining. After FU2.4 (5 swept): 14. After remaining small modules: ~1 (rules-only, FE-blocked).

---

**Test Summary:**
Architecture 99/99 PASS (14s)
Identity.Application 243→245/245 PASS (~800ms)
Build 0E/0W

**Review:** 9.6/10 DONE clean (zero folds — 2nd consecutive clean review on ARCH sweep)

**Cadence Today:** 27 commits / 13 feature ships ~9.5h total
`cc620a76`(P7-FU1.2) → … → `c545a196`(ARCH-FU2.2) → `4c888aff`(ARCH-FU2.3)
