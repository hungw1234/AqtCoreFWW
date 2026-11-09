// Graph-aware auto-layout for the workflow designer canvas.
//
// Replaces the old "sort-the-array-and-lean-on-the-3-col-grid" approach (which assigned no
// coordinates and ignored edges + forks entirely) with a real directed-graph layout. Dagre
// assigns ranks left→right along the transition/branch topology and separates parallel-branch
// children into their own vertical lane, so a fork no longer overlaps the main flow line.
//
// Pure + side-effect-free: takes the graph, returns coordinate maps. The caller owns persistence
// (writes the coords into state/branch DTOs through the normal save path). Layout is OPT-IN —
// invoked only from the toolbar "auto-layout" action, never on every render — so it never fights
// the user's manually dragged + persisted positions.

import dagre from '@dagrejs/dagre';
import type { WorkflowStateDto, WorkflowTransitionDto, WorkflowBranchDto } from '../workflow-types';
import { STATE_NODE_WIDTH, STATE_NODE_HEIGHT } from './react-flow-canvas-helpers';
import { makeGwId } from './gateway-node-build-utils';
import { DIAMOND_SIZE } from './custom-gateway-node';

// The fork gateway's dagre bounding box = the rendered diamond's side length (single source of truth).
const GATEWAY_SIZE = DIAMOND_SIZE;

// Left→right flow. ranksep = horizontal gap between successive steps; nodesep = vertical gap between
// siblings sharing a rank (e.g. the two children of an AND fork). Tuned for breathing room on a
// canvas where state cards are 180×80 — wide enough that edge labels don't collide.
const RANK_SEP = 120;
const NODE_SEP = 56;
const EDGE_SEP = 24;

interface Pos {
  x: number;
  y: number;
}

export interface DagreLayoutResult {
  /** New top-left position per state id. */
  statePositions: Map<string, Pos>;
  /** New top-left position per branch id (the fork gateway node). */
  gatewayPositions: Map<string, Pos>;
}

/**
 * Compute a left→right layered layout over the workflow graph.
 *
 * Feeds dagre BOTH the real transitions AND the branch structural edges (state→fork→child→
 * continuation) so the fork gateway and its children are ranked + laned by the same engine that
 * positions the main flow. Dagre internally breaks cycles (recall transitions), so a recall edge
 * never throws — it is simply reversed for ranking purposes.
 *
 * Coordinates returned are TOP-LEFT (React Flow's anchor); dagre reports node CENTER, so we shift
 * by half the node's measured box.
 */
export function computeDagreLayout(
  states: WorkflowStateDto[],
  transitions: WorkflowTransitionDto[],
  branches: WorkflowBranchDto[],
): DagreLayoutResult {
  const g = new dagre.graphlib.Graph();
  g.setGraph({ rankdir: 'LR', ranksep: RANK_SEP, nodesep: NODE_SEP, edgesep: EDGE_SEP });
  g.setDefaultEdgeLabel(() => ({}));

  const stateIds = new Set(states.map((s) => String(s.id)));

  // ── Nodes ──────────────────────────────────────────────────────────────────
  for (const s of states) {
    g.setNode(String(s.id), { width: STATE_NODE_WIDTH, height: STATE_NODE_HEIGHT });
  }
  for (const b of branches) {
    g.setNode(makeGwId(b.id), { width: GATEWAY_SIZE, height: GATEWAY_SIZE });
  }

  // ── Edges: real transitions ──────────────────────────────────────────────────
  for (const tr of transitions) {
    const from = String(tr.fromStateId);
    const to = String(tr.toStateId);
    if (stateIds.has(from) && stateIds.has(to)) g.setEdge(from, to);
  }

  // ── Edges: branch structure (matches buildGatewayEdges topology) ──────────────
  for (const b of branches) {
    const gwId = makeGwId(b.id);
    const src = String(b.branchingStateId);
    const cont = String(b.continuationStateId);
    if (!stateIds.has(src) || !stateIds.has(cont)) continue;

    g.setEdge(src, gwId); // branching state → fork
    for (const child of b.children) {
      const tgt = String(child.targetStateId);
      if (!stateIds.has(tgt)) continue;
      g.setEdge(gwId, tgt); // fork → child target
      g.setEdge(tgt, cont); // child → continuation (join)
    }
  }

  dagre.layout(g);

  // ── Read back, converting center → top-left ──────────────────────────────────
  const statePositions = new Map<string, Pos>();
  for (const s of states) {
    const node = g.node(String(s.id));
    if (!node) continue;
    statePositions.set(String(s.id), {
      x: Math.round(node.x - STATE_NODE_WIDTH / 2),
      y: Math.round(node.y - STATE_NODE_HEIGHT / 2),
    });
  }

  const gatewayPositions = new Map<string, Pos>();
  for (const b of branches) {
    const node = g.node(makeGwId(b.id));
    if (!node) continue;
    gatewayPositions.set(String(b.id), {
      x: Math.round(node.x - GATEWAY_SIZE / 2),
      y: Math.round(node.y - GATEWAY_SIZE / 2),
    });
  }

  return { statePositions, gatewayPositions };
}
