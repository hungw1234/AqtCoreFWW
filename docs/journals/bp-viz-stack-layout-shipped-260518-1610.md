# BP Workflow Viz: Layout Shipped, Canvas Reveal Decoupled

**Date:** 2026-05-18 16:10  
**Severity:** Medium  
**Component:** Business Process Form-Maps visualization (`web/src/features/business-process/business-process-form-maps-view.tsx` + `bp-workflow-flow-readonly.tsx`)  
**Status:** Resolved (layout shipped; reveal defect tracked separately)

## What Happened

A trivial layout request—move the workflow visualization block from a cramped left column to full-width below the form-config block—re-exposed a pre-existing react-flow v12 integration defect. The work touched down on four distinct architectural approaches, all failed in the new stacked layout, and hit a hard wall at attempt #3 that forced a choice: either re-architect to "fix" the measurement assumption or decouple and ship the verified layout.

## The Brutal Truth

This is frustrating because the layout change itself is one line of JSX. But every attempt to solve the canvas reveal defect added assumptions that sounded plausible *until we looked at the data*, and the data killed each one. Worse, the failed re-architect (the user's chosen direction) was built on the *same unverified premise* that had sunk the earlier approaches—a premise that cargo-cult reasoning had made invisible. The real kick in the teeth is that the original "BUG1" fix only ever *worked* by timing luck; it never fixed the root cause. We shipped layout; we're deferring the reveal defect; and we're documenting what actually killed each approach instead of guessing.

## Technical Details

**What shipped:** Full-width stacked layout in edit mode. Form-config Tabs + Save button (full-width, ~1131px, y=512). Canvas block below in a vertical stack (full-width, 1129×460, y=772). No Col grid, no flex-fill parent, no logic change. Layout verified live on the real code path. FE typecheck clean.

**What failed to ship:** Canvas node-reveal in the stacked layout. Four approaches:

1. **`useNodesInitialized` gate:** Hook flag never flips. Canvas silent on mount.
2. **`useUpdateNodeInternals(ids) + rAF`:** Public hook documented as forcing sync measure—*does not*. Defers to dead per-node ResizeObserver.
3. **`useStoreApi().updateNodeInternals(Map{nodeElement,force:true}) + rAF + wrapper ResizeObserver`:** Correct mechanism (proven to populate `measured` 3/3 times manually). Trigger fires before `.react-flow__node` DOM exists; no later retry path because wrapper size stabilizes once → ResizeObserver fires once.
4. **Re-architect: gate `<ReactFlow>` mount on measured non-zero container (user-sanctioned).** Premise: "the designer works because its container is sized, so sizing the container will work here too." **Disproven by evidence:** container is provably sized (rfStore 1129×460, node DOM box 170×78) yet `measured:{}`, `nodesInitialized:false`. Primary-source store inspection (the cheap move, should have been done before approach #4 committed) proved container sizing was never the invariant.

**Root cause:** react-flow v12's per-node ResizeObserver never copies the correct node DOM dimensions (170×78, measured live) into the store on dynamic antd container mounts. The *only* mechanism that populates `measured` is a forced store `updateNodeInternals({nodeElement,force:true})`. The unsolved piece is *reliable self-terminating retry*—invoking it once the node DOM exists without thrashing.

## What We Tried

1. Gated canvas render behind `useNodesInitialized`—inert flag never became true.
2. Wrapped the form-maps layout in a ResizeObserver to trigger forced measure—deferred, late, and died when wrapper RO fired before DOM was ready.
3. Attempted a secondary rAF + store API invoke path with explicit element refs—correct mechanism but timing trap.
4. Committed to a full re-architecture (container sizing, lazy mount, store hydration gate) based on "the designer works, so sizing + re-arch should work here too."
5. Then read the store. Container sized. Nodes DOM-valid. `measured:{}`. Approach #4's premise was false.

## Root Cause Analysis

**Three layers of failure:**

1. **Unverified premise:** The decision to re-architect (approach #4) rested on a single claim: "the designer canvas works, therefore a properly sized container will work here." Never verified from source. Store inspection then disproved it instantly.

2. **Timing-luck fix:** The original "BUG1" fix (`useUpdateNodeInternals` + rAF) only succeeded in the old Col layout because the Col grid's ResizeObserver happened to fire at the right moment. It was never a robust fix, just a fortunate accident of layout context. Moving to a stacked layout removed that accident and exposed the fragility.

3. **No self-terminating retry:** The correct mechanism (forced store `updateNodeInternals`) works—we proved it 3/3 times manually. But all in-component approaches to invoke it automatically (gate, rAF, wrapper RO) either fire before DOM is ready or fire once and stop. The missing piece is a reliable "keep trying until `measured` is populated and non-zero, then stop" loop.

## Lessons Learned

1. **Verify load-bearing premises with evidence before building on them.** The re-architecture was sanctioned because it sounded architecturally sound. But a "sounds good" premise is just a guess. A 30-second store inspection (the cheap, direct check) would have killed it before we committed. We did it after three failed approaches and two committed directions. Do it before.

2. **Surface architectural conflict instead of silently pivoting.** At attempt #3 (hard wall), the evidence was clear: the entire "container sizing" family (approaches #3 and #4) was built on a false invariant. The user had chosen approach #4; the evidence contradicted it. The right move was to surface that conflict explicitly ("evidence says your chosen direction is built on a false premise; it points to the rejected approach instead") and let the user decide. We did. User chose to decouple, ship the verified layout, and track the reveal defect as a spike. Correct call, surfaced cleanly instead of hidden.

3. **Fragile "fixes" that depend on timing luck are debt.** The original BUG1 fix worked in one context by accident. That's not a fix; that's a ticking bomb. When the context changes, the "fix" fails and someone else inherits the mystery. Document that every "solution" that passes verification can still be fragile if its success depends on incidental timing rather than an invariant.

## Next Steps

1. **Spike:** Harden BP read-only canvas reveal as a standalone initiative, independent of layout. Candidates: (a) deterministic retry loop (rAF/interval, forced store `updateNodeInternals`, self-terminate on `nodesInitialized` + non-zero `measured`)—the only approach consistent with all evidence; (b) replace canvas mount strategy / upgrade react-flow or use static SVG for read-only; (c) remount via `key` on a post-layout-stable signal.
2. **Verify:** Designer canvas (`react-flow-canvas.tsx`) uses the same bare `fitView`. Does it actually render workflows today, or also ride on RO-timing luck? Not re-verified this session—don't assume the "designer works" premise holds under scrutiny.
3. **Track:** Canvas blank in stacked layout until spike lands. Form-config fully functional. Accepted consequence, surface clearly in UI or docs.

**Status:** RESOLVED (layout shipped verified; reveal defect decoupled and tracked; re-architect approach reverted to clean committed baseline to avoid abandoned experiments in diff)
