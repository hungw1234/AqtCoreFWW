// Pure build helpers for parallel-branch gateway nodes and edges.
// These are ADDITIVE — caller appends the returned arrays to existing state/transition
// nodes/edges, never replacing them. Gateway edges are built independently so they
// never pass through buildEdges' state-id guard (fork ids are not state ids).

import type { MarkerType } from '@xyflow/react';
import type { WorkflowBranchDto, WorkflowStateDto } from '../workflow-types';

// Node id prefix — used to route drag commits to branches (not states).
export const GW_PREFIX = 'gw-fork-';

export function makeGwId(branchId: string) {
  return `${GW_PREFIX}${branchId}`;
}

/** Extract branchId from a gateway node id — undefined if not a gateway id. */
export function parseBranchIdFromGwId(nodeId: string): string | undefined {
  return nodeId.startsWith(GW_PREFIX) ? nodeId.slice(GW_PREFIX.length) : undefined;
}

interface GwPosition { x: number; y: number }

const GW_OFFSET_X = 120;
const GW_OFFSET_Y = 80;

/**
 * Auto-derive fork position as the midpoint between the branching state and the average
 * of its children's target states, then offset right + down slightly so it does not
 * overlap the branching state node. Falls back to a sensible fixed offset when state
 * coords are missing.
 */
function derivePosition(
  branch: WorkflowBranchDto,
  stateMap: Map<string, WorkflowStateDto>,
): GwPosition {
  const src = stateMap.get(String(branch.branchingStateId));
  if (!src || src.positionX == null || src.positionY == null) {
    return { x: GW_OFFSET_X, y: GW_OFFSET_Y };
  }
  // Average x/y of children's target states that have a persisted position
  const childStates = branch.children
    .map((c) => stateMap.get(String(c.targetStateId)))
    .filter((s): s is WorkflowStateDto => !!s && s.positionX != null && s.positionY != null);

  if (childStates.length === 0) {
    return { x: src.positionX + GW_OFFSET_X, y: (src.positionY ?? 0) + GW_OFFSET_Y };
  }

  const avgX = childStates.reduce((sum, s) => sum + (s.positionX ?? 0), 0) / childStates.length;
  const avgY = childStates.reduce((sum, s) => sum + (s.positionY ?? 0), 0) / childStates.length;

  return {
    x: Math.round((src.positionX + avgX) / 2),
    y: Math.round((src.positionY + avgY) / 2),
  };
}

/** Shape of gateway node `data` payload. */
export interface GatewayNodeData {
  branch: WorkflowBranchDto;
  branchTypeLabel: string;
}

function branchTypeLabel(type: WorkflowBranchDto['branchType']): string {
  switch (type) {
    case 'And': return 'AND';
    case 'Or': return 'OR';
    case 'NOfM': return 'N/M';
    default: return type;
  }
}

/**
 * Build ONE gateway node per branch. Position priority:
 *   1. branch.positionX/Y (persisted, user dragged)
 *   2. sessionPositionMap (in-session drag, not yet saved)
 *   3. auto-derived from branching state + children coords
 */
export function buildGatewayNodes(
  branches: WorkflowBranchDto[],
  states: WorkflowStateDto[],
  sessionPositionMap?: Map<string, GwPosition>,
) {
  const stateMap = new Map(states.map((s) => [String(s.id), s]));
  return branches.map((branch) => {
    const gwId = makeGwId(branch.id);
    const persisted =
      branch.positionX != null && branch.positionY != null
        ? { x: branch.positionX, y: branch.positionY }
        : null;
    const session = sessionPositionMap?.get(gwId) ?? null;
    const position = persisted ?? session ?? derivePosition(branch, stateMap);
    const data: GatewayNodeData = {
      branch,
      branchTypeLabel: branchTypeLabel(branch.branchType),
    };
    return {
      id: gwId,
      type: 'workflowGateway' as const,
      position,
      draggable: true,
      data: data as unknown as Record<string, unknown>,
    };
  });
}

// Visual language for the branch-structure edges. Distinct from the solid grey/blue *transition*
// edges (CustomTransitionEdge) so the eye reads them as "this is the fork scaffold, not a workflow
// action": amber (matches the gateway diamond), dashed, thin, with a small arrowhead for direction.
// `smoothstep` matches the transition edges' curve family (they use getSmoothStepPath) so the two
// edge kinds stay visually consistent instead of mixing bezier + step curves.
const STRUCT_EDGE_TYPE = 'smoothstep';
const STRUCT_EDGE_STYLE = { strokeDasharray: '6,4', stroke: '#fa8c16', strokeWidth: 1.5 };
// 'arrowclosed' is the string value of MarkerType.ArrowClosed. Using the literal (with a type-only
// MarkerType cast) avoids a runtime value-import of the enum — which keeps this pure helper loadable
// under test mocks of @xyflow/react that stub the module without re-exporting MarkerType.
const STRUCT_EDGE_MARKER = { type: 'arrowclosed' as MarkerType, color: '#fa8c16', width: 16, height: 16 };

interface GatewayEdge {
  id: string;
  source: string;
  target: string;
  sourceHandle: string;
  targetHandle: string;
  type: string;
  style: Record<string, unknown>;
  markerEnd: typeof STRUCT_EDGE_MARKER;
}

// Anchor every structural edge on the right→left axis (source's right handle → target's left handle)
// so the path flows along the left→right layout instead of detouring through the default top/bottom
// handles. WITHOUT explicit handles React Flow falls back to the first-declared handle pair
// (target=Top, source=Bottom on both state + gateway nodes) — that vertical anchoring on a horizontal
// flow was the root cause of the swooping, crossing dashed lines.
function structEdge(id: string, source: string, target: string): GatewayEdge {
  return {
    id,
    source,
    target,
    sourceHandle: 'r-out',
    targetHandle: 'l-in',
    type: STRUCT_EDGE_TYPE,
    style: STRUCT_EDGE_STYLE,
    markerEnd: STRUCT_EDGE_MARKER,
  };
}

/**
 * Build edges for all branches:
 *   - branchingState → fork gateway  (e-{branchId}-in)
 *   - fork gateway → each child target state  (e-{branchId}-c-{childId})
 *   - each child target state → continuation state  (e-{branchId}-j-{childId})
 *
 * Edges are VISUAL only (no transition data) — they use the built-in `smoothstep` type, so no custom
 * edge registration is needed and they are NOT deletable like real transitions.
 */
export function buildGatewayEdges(
  branches: WorkflowBranchDto[],
  states: WorkflowStateDto[],
) {
  const validIds = new Set(states.map((s) => String(s.id)));
  const edges: GatewayEdge[] = [];

  for (const branch of branches) {
    const gwId = makeGwId(branch.id);
    const srcId = String(branch.branchingStateId);
    const contId = String(branch.continuationStateId);

    if (!validIds.has(srcId) || !validIds.has(contId)) continue;

    // branching state → fork node
    edges.push(structEdge(`e-${branch.id}-in`, srcId, gwId));

    for (const child of branch.children) {
      const tgtId = String(child.targetStateId);
      if (!validIds.has(tgtId)) continue;

      // fork → child target
      edges.push(structEdge(`e-${branch.id}-c-${child.id}`, gwId, tgtId));
      // child → continuation (join)
      edges.push(structEdge(`e-${branch.id}-j-${child.id}`, tgtId, contId));
    }
  }

  return edges;
}
