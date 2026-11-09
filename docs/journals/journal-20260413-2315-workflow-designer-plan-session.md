# Workflow Designer Sync + Layout — Planning Session

**Date**: 2026-04-13 23:15
**Severity**: High
**Component**: `/admin/workflow/:id` — DefinitionVisualDesignerTab, react-flow-canvas
**Status**: Planned, pending implementation

## What Happened

Two confirmed bugs in the workflow Visual Designer. No guessing required — root causes were immediately obvious from reading the code.

Bug 1: `useState(definition.xxx)` initializer runs once at mount. Tab never unmounts (`destroyInactiveTabPane={false}`), so adding transitions from the Transitions tab leaves the Designer state stale. User has to navigate away and back to see updates.

Bug 2: Canvas uses a naive 3-column grid with 1 edge per transition and only top/bottom handles. Result: edges pile on top of each other, states cluster in three columns regardless of workflow topology.

## The Brutal Truth

Both bugs are embarrassingly preventable. The `useState` initializer trap is React 101 — you don't use `useState(prop)` for props you expect to change unless you explicitly handle derived state. We shipped this without ever testing the cross-tab interaction flow. The layout issue is worse: a 3-column grid for a graph visualization tool means the feature was never really finished, it was just "good enough to demo."

## Key Decisions

**Bug 1 — Hybrid A+B:** `useEffect` to sync on `definition` change guarded by `!hasChanges`, plus `key={definition.id}` to hard-reset on definition switch. Rejected pure `key` reset (loses local draft on every external change) and rejected lifting state to parent (over-engineering for a sync problem). Critically: switched guard to `useRef` after red-team round 2 caught that `useEffect([hasChanges])` dep would wipe undo history on every edit — a silent data-loss bug.

**Bug 2 — Dagre TB + 8 handles:** Dagre for automatic ranked layout, 8 directional handles per node to prevent edge collision, bezier curvature spread across handle pairs, SVG arc for self-loops. `fitView` moved from prop to `onInit` callback after round 2 caught that the prop resets viewport on every render.

**Deferred (YAGNI):** Drag persistence (R7) and draw-to-connect (R11) kicked to backlog as T5-20. Fixing layout ≠ building a full graph editor.

## Lesson Learned

Single-pass red-team review missed Q2 (Undo wipe) and Q5 (sibling rank routing dip). Both were caught in round 2. For UI state bugs involving `useEffect` dependency arrays and graph routing edge cases, one review pass is not enough. The extra token cost of a second adversarial pass is worth it when the failure mode is silent data loss.

## Next Steps

- Implementation: `/ck:cook plan.md` — phase-01 (~1h state sync), phase-02 (~4-6h layout)
- Owner: dev session next cycle
- Plans at: `C:\AqtCoreFW\plans\260413-2250-workflow-designer-sync-layout\`
- T4-04 expanded (FE↔BE validator drift scan, 7 gaps), T5-20 added to backlog
