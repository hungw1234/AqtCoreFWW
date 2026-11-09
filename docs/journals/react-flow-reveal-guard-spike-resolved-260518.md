# react-flow v12 blank-canvas reveal guard RESOLVED

**Date**: 2026-05-18 17:52  
**Severity**: High  
**Component**: Workflow visualization (BP/Designer)  
**Status**: Resolved (v2.137p66)

## What Happened

Blank canvas on BP-edit / workflow-designer antd Panel mounts. User sees white screen, no nodes render. Prior sessions identified react-flow v12 ResizeObserver invariant as root cause (v2.137p63-p65 OPEN SPIKE) but deferred solution.

## The Brutal Truth

This is fundamentally a library design trap. react-flow v12 exposes a public hook (`useUpdateNodeInternals`) that is **inert by design** — it defers to a dead ResizeObserver. Real fix requires private store access (`useStoreApi().getState().updateNodeInternals()`), which means coupling to internal API stability. No way around it without forking or waiting for upstream fix. Shipping a runtime poll feels hacky, but it's the only practical solution given constraints.

## Technical Details

**Root invariant** (@xyflow/react@12.10.1):
- Per-node ResizeObserver never copies DOM box to store state → `node.measured` remains `{}`
- `nodesInitialized` selector never flips true → queued `fitView()` never drains queue
- On antd `display:none` mount (Panel collapsed initially), observer callback fires but DOM rect is `0x0` → update silently fails
- Only mechanism: forced `updateNodeInternals(Map<id,{id,nodeElement,force:true}>,{triggerFitView:true})`

**Store API contract** (source-verified via node_modules typings):
```
getState().updateNodeInternals(
  Map<string,{id:string,nodeElement:HTMLElement,force:boolean}>,
  {triggerFitView?:boolean}
)
```

## What We Tried

**Prior session (v2.137p63-p65)**: Identified invariant, deferred solution. Attempted in-effect `fitView()` polls but inert (no node data).

**This session**:
1. **Scout node_modules**: Confirmed store API signature, `[data-id]` selector in DOM, component provider structure. Validated all 5 assumptions, corrected 1 (fitView NOT auto-drained → must pass `triggerFitView:true`).
2. **New shared hook** `use-reactflow-reveal-guard.ts`:
   - Bounded rAF poll (MAX_FRAMES=90 ≈1.5s window)
   - Self-terminates on `nodesInitialized` flip OR frame cap
   - Empty-graph guard (skip if no nodes)
   - Returns void (side-effect only)
3. **Integration**: Wired to `bp-workflow-flow-readonly.tsx` (replaced inert effect). Defensive port to `react-flow-canvas.tsx` (designer shared latent defect, masked by Tabs timing luck).

## Root Cause Analysis

Library gap: react-flow v12 shipped public API that doesn't work. ResizeObserver integration assumes persistent DOM visibility — breaks on `display:none` → reflow. No recovery path in public API. Forced to reach into private store.

## Lessons Learned

**Scout before code** (6-8× ROI): Reading node_modules typings before implementation prevented 3+ rework cycles. Assumptions were 80% correct; direct verification caught the `triggerFitView` parameter requirement.

**Trust but verify subagents**: This session caught TWO stale subagent claims:
- git-manager fabricated a pre-commit `git status` (tool never executed)
- docs-manager inflated LOC counts

**Shared-hook pattern for latent defects**: Both readonly and designer surfaces shared the bug. DRY hook + opt-in `refitOnRearm` flag (readonly=true, designer=false) preserves viewport intent while fixing reveal.

## Next Steps

1. Monitor prod telemetry for blank-canvas 404s (reveal logic runtime-only, no compile-time guard)
2. Plan Q2 surface audit: both BP-edit and designer likely share more latent visibility defects
3. Consider upstream issue to xyflow maintainers (store API stabilization)

## Unresolved / Residual Risk

- **Reveal logic coverage**: No integration test for `display:none` mount path (would require fixture-side antd Panel mock). Added console.warn on cap hit; ops must alert if it fires.
- **MAX_FRAMES=90 failure**: If user device slow or GC pauses exceed 1.5s window, canvas stays blank + console warn. No fallback (user refresh required). Low probability but possible under load.
- **Viewport re-arm flips**: Designer re-arm on external state-ADD flips `nodesInitialized:false` → guard re-fits, discarding user pan/zoom. Mitigated via `refitOnRearm:false` in designer, but Q2 should fix at source (prevent state-ADD flip if already initialized).

## Metrics

- Code-reviewer: 8.5/10 (caught re-arm defect, M1 mitigation sufficient)
- tester: 9/9 suite green (mock gap fixed: added `useStoreApi()` to fixture)
- typecheck: clean
- eslint: 0 errors
