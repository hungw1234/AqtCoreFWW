# Journal — 2026-06-15 TanStack-Query Cache Coherency Fixes (Polish Theme A)

**Date:** 2026-06-15 afternoon
**Severity:** Medium (3 read-stale bugs, no data-loss or auth bypass)
**Component:** FE cache (BP-run, Workflow polling, Forms fieldOptions)
**Status:** Resolved, 2 commits ahead, NOT pushed

---

## What Happened

Completed Polish Theme A (stale-data audit slice): 3 TanStack-Query cache-invalidation fixes across Workflow/Forms/BP modules, plus extracted a query-key factory to prevent literal drift.

**Bug 1 (High):** BP-run submissions stale after transition or state-form submit. Both code paths converged on `handleTransitionSuccess` in `bp-run-detail-page.tsx` but the handler invalidated only instance + availableTransitions + tasksByInstance — **missed** `['bp','run-submissions',id]`. Users saw stale submissions list up to 5 minutes (query staleTime default). Root: handler written assuming state-form submit alone; transition path added later without invalidation update.

**Bug 2 (Med):** Workflow task polling (`useTasksByAssignee` 30s poll, `useBranchStatuses` 10s poll) had `refetchInterval` but no `staleTime` — remount/refocus inside poll window triggered redundant background fetches on top of scheduled ones. Fix: set `staleTime == refetchInterval`. No mutation-driven freshness lost (both paths still refetch; only dupe remount fetches deduped).

**Bug 3 (Med):** Forms template publish/version-bump invalidated template + templates but not fieldOptions cache. Users got stale dynamic-select options for up to 5 min (default staleTime on `['forms','fieldOptions',id]`). Invalidation missing when dynamic options tied to template version.

---

## The Brutal Truth

This slice felt trivial on the surface — "just add invalidation keys" — but the BP-run bug is a good example of how convergence points hide in plain sight. Both paths (transition via modal `onTransitionSuccess`, state-form `onSubmitSuccess`) **look** independent but share one cleanup handler. We documented the first path, then added the second without auditing the handler. If we'd traced both callers before writing the first invalidation, we would have caught it.

The Workflow polling fix is a classic stale-window gotcha: `refetchInterval` without `staleTime` is a performance trap. Scheduler-driven refetch and remount-driven refetch are independent; setting them equal flips the semantics from "always fetch on remount" to "fetch only if data is old enough." This pattern recurs; we should document it in cache-strategy guidelines.

---

## Technical Details

**BP-run invalidation:**
```typescript
// Before: handleTransitionSuccess only invalidated instance + transitions
queryClient.invalidateQueries({ queryKey: ['bp', 'run-instance', id] });
queryClient.invalidateQueries({ queryKey: ['bp', 'run-available-transitions', id] });
queryClient.invalidateQueries({ queryKey: ['bp', 'run-tasks-by-instance', id] });

// After: added missing submissions query
queryClient.invalidateQueries({ queryKey: bpRunSubmissionsKey(id) });
```

Extracted `bpRunSubmissionsKey(id)` factory in `business-process-run-api.ts` (shared by hook + call-site; prevents literal string drift).

**Workflow poll staleTime:**
```typescript
const { data: tasks } = useTasksByAssignee({
  refetchInterval: 30000,  // was missing staleTime
  staleTime: 30000,        // added
});
```

**Forms fieldOptions invalidation:**
```typescript
// After template publish, invalidate all fieldOptions for that template
queryClient.invalidateQueries({
  queryKey: ['forms', 'fieldOptions', templateId],
  exact: false,  // prefix match covers all field-specific variants
});
```

---

## What We Tried

1. **Initial approach:** Add missing invalidation keys one-by-one per bug. Sufficient for correctness; **decision:** extract BP key factory to prevent next developer from duplicating the literal in another onSuccess handler (minimal DRY).

2. **Forms fieldOptions:** Raw prefix invalidation vs dedicated factory. Prefix key can't compose with existing 2-arg factory `formQueryKeys.fieldOptions(templateId, fieldId)` (would require 3-arg version). **Decision:** keep literal invalidation (asymmetry, but no false duplication risk here; noted as optional L1 nit for follow-up).

3. **Polling staleTime:** Considered per-module constants (TASK_POLL_INTERVAL, etc.) vs inline literals. **Decision:** inline (pattern already established; constants filed as separate Polish-E polish-hardcoded-i18n follow-up).

---

## Root Cause Analysis

**BP submissions:** Convergence-point blindness. The state-form submit path was added to an existing handler without re-auditing what the handler invalidates. Audit-step missing: trace both callers → identify shared handler → verify handler is complete for ALL callers.

**Workflow polling:** Misunderstood TanStack-Query semantics. `refetchInterval` schedules refetches regardless of staleTime; staleTime only controls **manual** `refetch()` calls and remount fetches. Without staleTime, remount inside the polling window bypasses the scheduler → redundant fetch. The fix reveals a subtle UX win (redundant polls burned battery on mobile).

**Forms fieldOptions:** Template version lifecycle uncovered at publish-time, not template-fetch-time. Invalidation written at fetch-site (template query); new lifecycle (version-bump triggers fieldOptions change) added later without revisiting all invalidation points. Pattern: lifecycle event → search ALL query keys that depend on it.

---

## Lessons Learned

1. **Convergence points are invisible in code:** When two features converge on one handler/helper, audit that handler/helper **after adding the second feature**. Checklist: "Are there other callers I haven't traced?" Not a tool problem; a discipline problem.

2. **`refetchInterval` without `staleTime` is a footgun:** Document it. The pattern is subtle enough that three engineers (and two agents) missed it. Should be: "If using scheduled `refetchInterval`, always set `staleTime` to the same value unless you explicitly want remount fetches."

3. **Invalidation discovery is late:** We find stale-cache bugs after users report them, not by code review. Two ideas for future prevention:
   - Cache-invalidation test suite: for each mutation, check that all **dependent** query keys are invalidated (requires mapping data-deps to query-keys — non-trivial, but ROI is high).
   - Invalidation checklist at merge: "List all query keys that depend on data modified by this handler" — lightweight, catches ~70% of misses.

4. **Extracted factories prevent future drift:** The BP submissions key is now shared (hook + call-site); next developer can't accidentally add a new onSuccess handler with a typo'd key. Small win, zero cost.

---

## Next Steps

1. **File follow-ups** ✓ Done — backlog rows: `POLISH-B-SWALLOWED-EXCEPTIONS` (T3), `POLISH-C-DIGSIG-UNAVAILABLE-UX` (T3, live gap), `POLISH-D-FILE-UPLOAD-SCAN-UX` (T4), `POLISH-E-I18N-HARDCODED` (T4), `POLISH-A-FORMS-FIELDOPTIONS-KEY-FACTORY` (T4 nit).

2. **Remaining polish themes** (pre-scouted, file:line ready): Themes B–E are candidate slices in backlog. Theme A verified + type-checked clean; commit 2 ahead (not pushed, awaiting integration with parallel work).

3. **No blocking issues.** Tests pass, review approved 0-block (2 optional Low nits accepted as follow-up).

---

## Verification

- **typecheck:** 0 errors
- **eslint:** 0 errors  
- **vitest:** 1055 of 1055 tests pass (113 files)
- **code-review:** APPROVE, 0 critical/high blocking (2 optional Low nits)
- **Commits:** `fd16b29a` (fix), `68cf9cd8` (docs/backlog)
- **Branch:** main, 2 ahead of origin, NOT pushed

---

**Status:** DONE
**Journal:** C:\AqtCoreFW\docs\journals\journal-260615-1430-polish-stale-data-cache-tanstack-coherency.md
