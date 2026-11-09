# P7-FU1.2: Workflow Pickers Swap to Select w/ Display Names

**Date**: 2026-05-17 08:50  
**Severity**: Medium  
**Component**: BP Admin FE + Workflow module client  
**Status**: Resolved  
**Commit**: `cc620a76`  
**Version**: v2.137p34

---

## What Happened

P7-FU1.2 shipped form-map designer picker upgrades: state/transition pickers in the admin UI no longer ask users to enter raw bigint IDs. Instead, Select components display human-readable `displayNameVi`/`displayNameEn` labels sourced from the existing workflow detail endpoint.

Wallclock: ~30-40min (session 08:10-08:45 ICT).

---

## The Brutal Truth

A hidden blessing: the original 2h+1h estimate (option b — new endpoint) evaporated when scout caught that `GET /workflow/definitions/{guid}` already ships the full state/transition tree with display names. Nobody reads backlog closely before planning. FE would have wasted a day building picker logic against a nonexistent data source if we'd committed to the wrong approach upfront.

---

## Technical Details

**Backend additions**
- `IWorkflowModuleClient.TryGetDefinitionPublicIdAsync(long tenantId, long internalId) → Guid?`  
  Inverse of existing `TryResolveDefinitionInternalIdAsync`. Reuses existing `GetWithStatesAndTransitionsByInternalIdAsync` repo call (no new SQL). Returns null silently on cross-tenant attempt — no leak.

- `BusinessProcessDetailDto.WorkflowDefinitionPublicId: Guid?`  
  Nullable to tolerate orphan FKs. `GetBusinessProcessByIdQueryHandler` resolves after Dapper root fetch via `IWorkflowModuleClient`.

- `BusinessProcessRootRow` extracted from nested private to namespace scope  
  Mirrors P4 Slice 2B `GetIntakeFormSchemaRow` precedent. Enables NSubstitute mocking in `GetBusinessProcessByIdQueryHandler` tests.

**Frontend additions**
- `business-process-form-maps-view` fetches `BpDefinitionDto.workflowDefinitionPublicId`
- State/transition Select components replace InputNumber; `useMemo` memoizes option arrays from workflow definition states/transitions
- 5 i18n keys added (vi+en): `formMaps.col.state`, `formMaps.col.transition`, `formMaps.edit.selectState`, `formMaps.edit.selectTransition`, `formMaps.edit.noWorkflowDefinition`

**Test deltas**  
- BP.Application 78→82 (+4 facts: cross-tenant short-circuit, not-found, happy-path PublicId propagation, orphan-FK null)
- Workflow.Infrastructure +3 facts (happy/not-found/cross-tenant for new method)
- Architecture 97/97 + FE typecheck: PASS

---

## Root Cause Analysis

Original plan scope (option b) assumed new endpoint delivery was necessary. Scout reading `GetWorkflowDefinitionQueryHandler` at line ~40 revealed existing response already carried `states: [ { displayNameVi, displayNameEn, ... } ]`. **The information was already there; the plan didn't know to look for it.**

This happened because:
1. Backlog options didn't hint at existing endpoint behavior
2. No "verify existing endpoints before proposing new ones" gate in pre-cook
3. Plan wrote the estimate before scout ran

---

## What We Tried

**Option A (original b1)**: Build new `/api/v1/workflow/definitions/{id}/form-map-picker-options` endpoint returning flattened `{ stateId, displayName }[]`.  
*Rejected by scout*: Duplication; existing endpoint has the data.

**Option B2 (chosen)**: Surface `WorkflowDefinitionPublicId` in existing `BusinessProcessDetailDto`. FE uses existing `GET /workflow/definitions/{guid}` endpoint to populate pickers.  
*Chosen*: ~80% effort reduction. Reuses existing infrastructure.

---

## Lessons Learned

**L1: Scout BEFORE plan-finalize saves 80% scope**  
The pivot happened only because we read handler code mid-session. Codify: pre-cook scout should always include "what do existing cross-module endpoints return?" for any inter-module story. Would have caught this before locking estimates.

**L2: Cross-module-call addition triggers multi-file ripple**  
`GetBusinessProcessByIdQueryHandler` previously had zero external client dependencies. Adding `IWorkflowModuleClient` constructor param rippled through: (a) handler constructor signature, (b) test mock plumbing, (c) namespace extraction of `BusinessProcessRootRow` for assertable mocking. Small change, multi-file surface. Worth flagging in future handler reviews.

**L3: i18n keys must be paired with locale files same edit batch**  
M-1 finding: 5 new keys in FE code but missing from `vi.json`/`en.json`. Caught only at review; defaultValue fallback masked the gap during dev. Codify: any `t('new.key')` addition triggers same-edit cascade to both locale files. Consider lint rule to enforce.

**L4: Review-score fold-in ROI continues**  
Initial 9.0 → 9.5+ after folding M-1 (i18n keys) + M-2 (handler test asserts) inline. Two M-class nits cleared in ~15min vs two follow-ups. The 5+1 fold rule on additive features sustains high review bar without accumulating debt.

---

## Next Steps

**Follow-ups filed (Low/Nit)**
- P7-FU1.2.1 (~10 LOC): Vi-only fallback in picker labels when En missing
- P7-FU1.2.2 (~20 LOC): ReadOnlyTabs still shows raw bigint IDs in detail view
- P7-FU1.2.3 (Low): Distinguish "no workflow" from "fetch failed" in picker spinner
- P7-FU1.2.4 (Nit): Inline comment for bigint→Number JS conversions

**Cleanup debt from prior sessions**
- Uncommitted journal bundle: `journal-260517-0801-bp-mvp-p8-p7-fu-session-bundle-v2137p29-p33.md`
- Uncommitted test artifact: `web/playwright-report/index.html`
- Flag both for next session cleanup or delete before final ship.

---

## Metrics

| Metric | Before | After |
|--------|--------|-------|
| Wallclock | — | 30-40min |
| BE test facts | 78 | 82 |
| FE i18n keys (pending) | 0 | 5 |
| Estimate reduction vs option (b) | 3h | — |
| Review score | 9.0 | 9.5+ |
| Files changed | — | 14 |
| Build errors | — | 0 |

---

## Files Changed

```
git show cc620a76 --stat
 backlogs/backlog.md
 docs/dependency-map.md
 docs/development-roadmap.md
 docs/project-changelog.md
 src/modules/businessprocess/.../BusinessProcessDtos.cs
 src/modules/businessprocess/.../GetBusinessProcessByIdQueryHandler.cs
 src/modules/businessprocess/.../InProcessWorkflowModuleClient.cs
 src/modules/businessprocess/.../BusinessProcessDetail*.cs (test)
 src/modules/workflow/...
 web/src/.../business-process-form-maps-view.tsx
 web/src/locales/en.json
 web/src/locales/vi.json
 [+7 more, 14 total, 324 insertions +37 deletions]
```

Full diff: `git show cc620a76`

---

## Decision Log

| ID | Decision | Rationale | Alternative |
|---|----------|-----------|-------------|
| D1 | Pivot to option (b2): surface PublicId in DTO | Existing endpoint already returns states+transitions with names; ~80% scope cut | Build new picker-options endpoint (b1) |
| D2 | TryGetDefinitionPublicIdAsync returns Guid? (null on cross-tenant) | Silent fail mirrors precedent; no existence leak; caller checks null | Throw exception; use Result<> |
| D3 | Reuse existing repo call, no new method | KISS; repo call already available via `GetWithStatesAndTransitionsByInternalIdAsync` | New dedicated repo query |
| D4 | Extract BusinessProcessRootRow to namespace scope | Enables NSubstitute mocking of IReadDbConnection calls; mirrors P4 precedent | Keep nested private; use reflection mocking |

---

**Status**: SHIPPED  
**Review**: 9.5/10 APPROVE_WITH_NITS  
**Concerns**: None blocking; follow-ups are Low/Nit priority and non-critical to admin usability.
