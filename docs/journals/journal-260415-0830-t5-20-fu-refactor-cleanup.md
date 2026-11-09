# T5-20 Follow-up: FU1 + FU2 Refactor Cleanup

**Date**: 2026-04-15 08:30
**Severity**: Medium
**Component**: Workflow Designer (React Flow, TransitionEditorModal, visual designer tab)
**Status**: Resolved

## What Happened

Post-code-review cleanup from T5-20 (commit `3e14b90a` on `main`). Two targeted refactors:
- **FU1**: `TransitionEditorModal` prop hardening — `initialFields?: { fromStateId?: string; toStateId?: string }` + gỡ unsafe `as WorkflowTransitionDto | null` cast in draw-to-connect handler
- **FU2**: LOC extraction — `react-flow-canvas.tsx` 198→152 lines; `definition-visual-designer-tab.tsx` 193→148 lines via 2 new helpers + handler hook

## The Brutal Truth

Micro-refactors feel pedantic until they're not. FU1 exposed a React 18 batching subtlety that saved us from future race conditions. FU2 drained 2 hours wrestling with reference identity in tests — export re-wrapping felt absurd until it prevented import path creep across 12 test files.

## Technical Details

**FU1 – Prop Narrowing:**
- antd `Form` + `destroyOnHidden` re-reads `initialValues` on every mount cycle
- Before: `TransitionEditorModal` accepted `dto: WorkflowTransitionDto | null`; draw-to-connect built partial DTO then `as` cast
- After: Accept `initialFields?: { fromStateId?: string; toStateId?: string }`; Form destructures safely
- No `key` force-remount needed; React 18 batching protects field order + state ordering

**FU2 – Extraction Breakdown:**
- `react-flow-canvas-helpers.ts` (61 LOC): `buildNodes`, `buildEdges`, `applyStepDefaults`, `isTransitionValid`
- `use-visual-designer-handlers.ts` (102 LOC): `useVisualDesignerHandlers` hook (select/update handlers) — keeps reactive closure over `states`/`transitions` arrays
- Kept handlers inline (not extracted) because they close over mutable refs; extraction would break.
- Re-export from `react-flow-canvas.tsx`: `export { buildNodes }` etc. — stable reference identity, tests unchanged

## What We Tried

1. Extract ALL handlers to hook — handler dependencies exploded, needed per-handler `useMemo` wrappers. Reverted.
2. Force `key` remount on `initialFields` change — unnecessary when batching prevents race conditions.
3. Move handlers to helpers + `useCallback` with param object deps — **[p] pattern hit**: rebuilt object each render breaks memoization silently. Fixed by destructuring params + relying on setter/ref identity.

## Root Cause Analysis

**Silent memoization breaker**: `useCallback(fn, [obj])` where `obj` is a params object built in parent render invalidates memo every render, hiding the real intent. Correct pattern: destructure params into atomic props, rely on primitive-type stability.

**antd Form subtlety**: `destroyOnHidden=true` + mount-time `initialValues` read doesn't race with React 18 because batching ensures all setState calls complete before next commit phase. Initial assumption "we need `key` forcing" was overly defensive.

## Lessons Learned

1. **Dependency shape matters**: Passing `{ a, b }` to `useCallback` is dangerous if `a` or `b` are rebuilt. Destructure in caller or stabilize with `useMemo`.
2. **React 18 batching is your friend**: Don't force remounts with `key` when batching handles ordering. Trust the framework.
3. **Reference identity in exports**: Re-exporting helpers keeps test imports stable even if internal structure shifts. One export barrel > scattered imports across 12 test files.
4. **Hook closure semantics**: Reactive handlers that reference arrays must stay in hook scope; extracting them breaks closure binding. Not every inline function deserves extraction.

## Next Steps

- Mark T5-20 complete in backlog (v2.92→v2.93 ✓)
- Proceed to T5-16 (workflow cursor pagination, BE-heavy)
- Playwright E2E deferred (port 5002 offline — same lineage blocker as T4-04, T5-17, T5-19, T5-20)

## Verification

- `tsc src app --noEmit`: ✓
- `tsc src test --noEmit`: ✓
- Workflow vitest: 103/103 ✓
- Full vitest: 729/730 (1 flaky `cases` test unrelated, passes isolated)
- Code review: 8/10; 0 critical; 1 major M2 (destructured-deps fix applied)
