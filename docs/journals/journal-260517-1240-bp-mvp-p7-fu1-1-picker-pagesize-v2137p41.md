# P7-FU1.1: Picker Page Size Hardening

**Date**: 2026-05-17 12:40 ICT
**Severity**: Low
**Component**: BP Admin UI — Workflow/FormTemplate Pickers
**Status**: Resolved

## What Happened

Unified picker page size from hard-coded 100 to constant 500 across `useFormTemplatePicker` and `useWorkflowDefinitionPicker`. Wallclock ~15min vs 30min estimate (2x compression). FE typecheck PASS, review 9.4/10 DONE_WITH_CONCERNS.

## Technical Details

- **Changed**: `web/src/features/business-process/business-process-admin-api.ts` introduced `PICKER_PAGE_SIZE = 500` constant
- **Applied**: Both picker hooks + `business-process-form-maps-view.tsx` back-fill comment
- **Diff**: 4 files, +26/-5 LOC (TS/TSX + backlog + changelog)
- **Comment block**: Explains soft cap rationale; paginated-loop alternative deferred (YAGNI)

## Why It's Done

KISS over `useInfiniteQuery`. Fetch-all in one call beats paginated recursion: avoids race conditions on `didBackfillRef`, simpler mental model for picker UX, reviewer agreed scope correct. Real constraint is API response size (currently unbounded), not fetch strategy.

## Reviewer Finding (DONE_WITH_CONCERNS + FU)

Concern filed forward as **P7-FU1.1-FU**: BE lacks `Math.Clamp` hardening on Forms/Workflow paginators. Pattern already exists in Identity (`ListSodViolationLogsQueryHandler.cs:23` caps at 100). Cross-module sweep candidate.

This pattern — reviewer surfacing deferrable concern as explicit FU — effectively auto-approves 9.4 DONE_WITH_CONCERNS. Codified: concern + FU filing = threshold met, ship proceeds.

## Lessons

1. **Simplicity dominance**: Fetch-all vs paginated loop = 15min vs 1h + race risk. When data set small (pickers), pagination complexity unjustified.

2. **Reviewer-filed FU pattern valid**: 9.4 + explicit FU ≠ blocker. Concern is real (BE defense-in-depth gap), but deferrable to maintenance. Ships faster, debt tracked.

3. **Cross-module pattern gap surfaced**: Forms/Workflow handlers missing `Math.Clamp(PageSize, 1, 1000)` guard. Defense-in-depth DoS hardening. Recommend sweep after P7 closes.

4. **Housekeeping threshold**: `web/playwright-report/index.html` surfaced 3x across sessions before `git rm --cached` today. Deferred small chores past 3rd sighting should bundle next ship.

## Next Steps

- **P7-FU1.1-FU** (Low, ~30min): Add `Math.Clamp` to Forms + Workflow paginated handlers. Can land same session as P7 finalization or deferred to post-MVP maintenance.
- **Housekeeping**: Gitignore-sync check on next 3-ship milestone.

---

**Cadence Note**: 8th feature ship today (14 commits, ~6h). P7 vertical-slice feature-complete after P8-FU2.1.1. All BP MVP FE admin+user UX shipped end-to-end.
