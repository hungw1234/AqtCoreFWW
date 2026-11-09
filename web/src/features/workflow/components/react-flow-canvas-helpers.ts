// Pure helpers + type registrations for ReactFlowCanvas.
// Extracted to keep canvas component within LOC ceiling.

import type { WorkflowStateDto, WorkflowTransitionDto, WorkflowBranchDto } from '../workflow-types';
import type { GraphViolation } from '@/core/api/api-error';
import { CustomStateNode } from './custom-state-node';
import { CustomTransitionEdge } from './custom-transition-edge';
import { CustomGatewayNode } from './custom-gateway-node';

// Node/edge type keys — defined at module scope to prevent re-registration on every render
export const NODE_TYPES = {
  workflowState: CustomStateNode,
  workflowGateway: CustomGatewayNode,
} as const;
export const EDGE_TYPES = { workflowTransition: CustomTransitionEdge } as const;

// Shape of the `data` payload carried on each workflow-state node. Extends the raw state DTO
// with transient graph-validation annotations injected by buildNodes — these are NOT persisted,
// they only drive the in-canvas error highlight after a failed save.
export type StateNodeData = WorkflowStateDto & {
  /** True when this state has ≥1 unresolved graph-validation violation (drives error glow + badge). */
  hasViolation?: boolean;
  /** The violations attached to this state (drives the hover tooltip). Null when none. */
  violations?: GraphViolation[] | null;
  /** True when a parallel branch may be authored on this state (non-final + not already forked).
   *  Drives the per-node fork affordance — the button is only rendered when this is true. */
  canFork?: boolean;
  /** Stable parent callback to author a fork on a given state id. The SAME ref is injected into
   *  every node's data (not a per-node closure) so React Flow's node memo isn't defeated; the node
   *  passes its own id back. Do NOT change this to `() => onAddFork(id)` — that breaks the memo. */
  onAddFork?: (stateId: string) => void;
};

/** Options bundle for the per-node fork affordance, threaded into buildNodes. */
export interface ForkNodeOptions {
  /** State ids that already own a branch (RB3 — at most one fork per state). */
  branchedStateIds: Set<string>;
  /** Stable callback (parent useCallback) to author a fork on the given state. */
  onAddFork: (stateId: string) => void;
}

export const STATE_NODE_WIDTH = 180;
export const STATE_NODE_HEIGHT = 80;
export const HORIZONTAL_GAP = 60;

// Build node list: prefers session positionMap (recent drag) → persisted DTO position
// (positionX/Y from BE) → grid fallback. Position priority preserves user's latest UI edit
// over server state during the same session (positionMap survives until save+refetch).
//
// violationsByStateName: transient map of graph-validation violations keyed by state *name*
// (the canonical code name). The mapping is name-based, NOT id-based: the backend graph
// validator reports violations against `state.Name` (never the display name), while nodes are
// keyed by `state.id`. buildNodes bridges that gap by joining on `s.name`. A state with entries
// here gets `hasViolation=true` and its violation list attached for the node's hover tooltip.
export function buildNodes(
  states: WorkflowStateDto[],
  positionMap?: Map<string, { x: number; y: number }>,
  violationsByStateName?: Map<string, GraphViolation[]>,
  forkOptions?: ForkNodeOptions,
) {
  const COLS = 3;
  return states.map((s, i) => {
    const sessionPos = positionMap?.get(s.id);
    const persistedPos =
      s.positionX != null && s.positionY != null
        ? { x: s.positionX, y: s.positionY }
        : null;
    const gridPos = {
      x: (i % COLS) * (STATE_NODE_WIDTH + HORIZONTAL_GAP),
      y: Math.floor(i / COLS) * (STATE_NODE_HEIGHT + 60),
    };
    const violations = violationsByStateName?.get(s.name) ?? null;
    // A fork is offerable only on non-final states (FE UX choice — a fork on a final state is
    // meaningless) that don't already own one (RB3 — one fork per state).
    // onAddFork is the SAME parent ref for every node (the node passes its own id) — see StateNodeData.
    const canFork =
      forkOptions != null && !s.isFinal && !forkOptions.branchedStateIds.has(s.id);
    const data: StateNodeData = {
      ...s,
      violations,
      hasViolation: (violations?.length ?? 0) > 0,
      canFork,
      onAddFork: forkOptions?.onAddFork,
    };
    return {
      id: s.id,
      type: 'workflowState' as const,
      position: sessionPos ?? persistedPos ?? gridPos,
      data: data as unknown as Record<string, unknown>,
    };
  });
}

// Build edges from transitions — preserves persisted handle anchors so reconnect survives reload.
//
// Defense-in-depth (BUG2-CLONE 2026-05-18): a transition whose fromStateId/toStateId does not
// match any state id is dropped + warned, instead of silently handed to react-flow (which renders
// nothing — the original "blank visualization" symptom of the WorkflowDefinition.Clone() FK=0
// corruption). The BE bug is fixed + data remediated; this guard makes any future recurrence (or
// stale corrupt data) VISIBLE in the console rather than an unexplained empty canvas.
export function buildEdges(
  transitions: WorkflowTransitionDto[],
  states: WorkflowStateDto[],
) {
  const validStateIds = new Set(states.map((s) => String(s.id)));
  return transitions
    .filter((tr) => {
      const ok =
        validStateIds.has(String(tr.fromStateId)) &&
        validStateIds.has(String(tr.toStateId));
      if (!ok) {
        console.warn(
          `[workflow] dropping transition ${tr.id} ("${tr.actionName}") — ` +
            `fromStateId=${tr.fromStateId}/toStateId=${tr.toStateId} not in definition states ` +
            `(corrupt/unresolved FK — see WorkflowDefinition.Clone fix 2026-05-18)`,
        );
      }
      return ok;
    })
    .map((tr) => ({
      id: tr.id,
      source: tr.fromStateId,
      target: tr.toStateId,
      sourceHandle: tr.sourceHandle ?? undefined,
      targetHandle: tr.targetHandle ?? undefined,
      type: 'workflowTransition' as const,
      data: tr as unknown as Record<string, unknown>,
    }));
}

export interface GraphChange {
  nodes: ReturnType<typeof buildNodes>;
  edges: ReturnType<typeof buildEdges>;
}

export interface ReactFlowCanvasProps {
  states: WorkflowStateDto[];
  transitions: WorkflowTransitionDto[];
  onGraphChange: (nodes: ReturnType<typeof buildNodes>, edges: ReturnType<typeof buildEdges>) => void;
  onNodeSelect: (stateId: string | null) => void;
  onEdgeSelect: (transitionId: string | null) => void;
  /** In-memory position map — survives refetch within same tab session (no page-reload persistence). */
  positionMap?: Map<string, { x: number; y: number }>;
  /** Transient graph-validation violations keyed by state *name* — drives the in-canvas error
   *  highlight after a failed save. Cleared on the next save attempt and on editing the node. */
  violationsByStateName?: Map<string, GraphViolation[]>;
  /** Called once per drag-end with the final committed position. */
  onPositionCommit?: (stateId: string, pos: { x: number; y: number }) => void;
  /** When provided, called instead of addEdge locally — parent opens TransitionEditorModal. */
  onCreateTransition?: (payload: { fromStateId: string; toStateId: string }) => void;
  /** Called when user reconnects any existing edge endpoint — passes new node ids AND
   *  new handle ids so both cross-node reroutes and same-node visual orientations persist. */
  onReconnectTransition?: (
    transitionId: string,
    update: {
      fromStateId: string;
      toStateId: string;
      sourceHandle: string | null;
      targetHandle: string | null;
    },
  ) => void;
  /** Parallel branches to render as gateway (fork) nodes + visual edges on the canvas. */
  branches?: WorkflowBranchDto[];
  /** Called when a gateway (fork) node drag-ends — branchId is the raw branch id
   *  (without the gw-fork- prefix). Parent writes positionX/Y back to the branch. */
  onGatewayPositionCommit?: (branchId: string, pos: { x: number; y: number }) => void;
  /** Authors a parallel branch on the given state. When provided, each forkable state node
   *  (non-final, not already forked) shows a hover/selected fork button. */
  onAddFork?: (stateId: string) => void;
  /** Monotonic counter — each increment requests the canvas re-frame all nodes (fitView). Bumped by
   *  the parent after an auto-layout so the freshly arranged graph lands in view. The initial value
   *  is ignored (fitView on mount is already handled by the `fitView` prop + reveal guard). */
  fitViewSignal?: number;
}
