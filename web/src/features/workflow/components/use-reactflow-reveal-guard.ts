// Shared react-flow v12 "reveal guard" — fixes blank-canvas when a <ReactFlow> mounts
// inside a display:none ancestor (antd Card/Tabs reveal).
//
// Root invariant (proven via live store inspection 2026-05-18, @xyflow/react@12.10.1):
// react-flow measures each node via a per-node ResizeObserver. When the canvas first
// mounts hidden, that RO never delivers an initial entry → node.measured stays {} →
// `nodesInitialized` never flips → the queued one-shot fitView never drains → every
// node stays visibility:hidden (blank canvas). Container size is NOT the cause.
//
// The ONLY mechanism that populates `measured` is the FORCED store action
// `updateNodeInternals(Map<id,{id,nodeElement,force:true}>)` (reads getBoundingClientRect
// directly, bypassing the dead RO). The public `useUpdateNodeInternals(ids)` hook does
// NOT force a sync measure — it defers to the same dead RO, so it is inert here.
//
// There is no reliable single DOM/layout event to hang the forced call on (the node DOM
// appears at an unknown later tick; container size is stable so no RO re-fire). A bounded,
// self-terminating rAF poll is the only approach consistent with all the evidence.
//
// Used by BOTH the BP read-only canvas (where the defect manifests) and the workflow
// designer (defensive — it currently renders only by Tabs-container timing luck).

import { useEffect, useRef } from 'react';
import { useStoreApi } from '@xyflow/react';

// ~1.5s at 60fps. Must cover the antd Card display:none → reveal delay on editMode
// transition; self-terminates early the moment `nodesInitialized` flips (typically a
// few frames once the node DOM exists), so the cap only matters on the failure path.
const MAX_FRAMES = 90;

interface RevealGuardOptions {
  /**
   * Whether a measure forced AFTER the first successful reveal should also re-run fitView.
   *
   * `nodes` changing identity (e.g. external save/undo re-sync that ADDS a state node)
   * re-arms the guard; react-flow flips `nodesInitialized` back to false for the unmeasured
   * newcomer, so the guard force-measures again. On that re-arm:
   *  - read-only canvas → `true` (default): re-fit is desirable — pan/zoom is navigation-
   *    only/ephemeral, showing the updated graph fitted is the wanted behavior.
   *  - designer canvas → `false`: the user owns the viewport (manual pan/zoom/drag);
   *    silently repair measurement but DON'T discard their viewport.
   * The very first reveal always fits regardless (the canvas must frame its initial graph).
   */
  refitOnRearm?: boolean;
}

/**
 * Polls (rAF) until react-flow's nodes are measured, forcing a synchronous re-measure
 * each tick via the store action. Self-terminates on `nodesInitialized` or the frame cap.
 *
 * MUST be called from a component rendered inside `<ReactFlowProvider>` (so `useStoreApi`
 * resolves). `nodes` is used only for its ids + as the re-arm dependency — pass the same
 * array the canvas renders so the guard re-runs when the graph changes.
 */
export function useReactFlowRevealGuard(
  nodes: readonly { id: string }[],
  { refitOnRearm = true }: RevealGuardOptions = {},
): void {
  const store = useStoreApi();
  // Persists across effect re-runs (component lifetime) — only the FIRST reveal is
  // unconditionally fitted; later re-arms honour refitOnRearm.
  const hasRevealed = useRef(false);

  useEffect(() => {
    if (nodes.length === 0) return; // empty graph — nothing to measure, don't poll

    let raf = 0;
    let frames = 0;

    const tick = () => {
      frames += 1;
      const state = store.getState();
      if (state.nodesInitialized) {
        hasRevealed.current = true;
        return; // measured — stop
      }

      // Structural shape matches @xyflow's InternalNodeUpdate ({id,nodeElement,force?});
      // typed locally to avoid importing react-flow internals.
      const updates = new Map<string, { id: string; nodeElement: HTMLDivElement; force: boolean }>();
      for (const n of nodes) {
        const el = document.querySelector<HTMLDivElement>(
          `.react-flow__node[data-id="${n.id}"]`,
        );
        if (el && el.offsetWidth > 0 && el.offsetHeight > 0) {
          updates.set(n.id, { id: n.id, nodeElement: el, force: true });
        }
      }
      if (updates.size > 0) {
        // First reveal always fits (frame the initial graph). Post-reveal re-arms honour
        // refitOnRearm — designer passes false so a measurement repair doesn't discard the
        // user's manual pan/zoom. triggerFitView drains the queued fitView atomically in
        // the same store cycle (no separate fitView() racing the measure).
        const triggerFitView = !hasRevealed.current || refitOnRearm;
        state.updateNodeInternals(updates, { triggerFitView });
      }

      if (frames >= MAX_FRAMES) {
        console.warn(
          '[rf-reveal-guard] gave up after',
          MAX_FRAMES,
          'frames; nodes still unmeasured — canvas may stay blank',
        );
        return;
      }
      raf = requestAnimationFrame(tick);
    };

    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [nodes, store, refitOnRearm]);
}
