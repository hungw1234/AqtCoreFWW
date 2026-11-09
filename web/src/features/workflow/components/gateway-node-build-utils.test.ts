// Vitest unit tests for gateway-node-build-utils.ts
// Covers: node/edge derivation counts, position priority (persisted > session > auto-derive),
// empty branches → no nodes/edges, edge id uniqueness, invalid state guard.

import { describe, it, expect } from 'vitest';
import {
  buildGatewayNodes,
  buildGatewayEdges,
  makeGwId,
  parseBranchIdFromGwId,
} from './gateway-node-build-utils';
import type { WorkflowBranchDto, WorkflowStateDto } from '../workflow-types';

// ── fixtures ──────────────────────────────────────────────────────────────────

const stateA: WorkflowStateDto = {
  id: 's1', name: 'Draft', displayNameVi: 'Nháp', displayNameEn: 'Draft',
  isInitial: true, isFinal: false, color: '#aaa', sortOrder: 0,
  positionX: 100, positionY: 200,
};
const stateB: WorkflowStateDto = {
  id: 's2', name: 'Review', displayNameVi: 'Xem xét', displayNameEn: 'Review',
  isInitial: false, isFinal: false, color: '#bbb', sortOrder: 1,
  positionX: 400, positionY: 200,
};
const stateC: WorkflowStateDto = {
  id: 's3', name: 'Done', displayNameVi: 'Xong', displayNameEn: 'Done',
  isInitial: false, isFinal: true, color: '#ccc', sortOrder: 2,
  positionX: 700, positionY: 200,
};
const stateD: WorkflowStateDto = {
  id: 's4', name: 'ParallelB', displayNameVi: 'Nhánh B', displayNameEn: 'Branch B',
  isInitial: false, isFinal: false, color: '#ddd', sortOrder: 3,
  positionX: 400, positionY: 400,
};

const branch1: WorkflowBranchDto = {
  id: 'b1',
  branchingStateId: 's1',
  continuationStateId: 's3',
  branchType: 'And',
  allowCancelOnAnyReject: false,
  timeoutMinutes: 0,
  positionX: null,
  positionY: null,
  children: [
    { id: 'c1', targetStateId: 's2', name: 'child1', displayNameVi: 'Nhánh 1', sortOrder: 0, resolutions: [] },
    { id: 'c2', targetStateId: 's4', name: 'child2', displayNameVi: 'Nhánh 2', sortOrder: 1, resolutions: [] },
  ],
};

const branchWithPosition: WorkflowBranchDto = {
  ...branch1, id: 'b2', positionX: 300, positionY: 150,
};

// ── id helpers ────────────────────────────────────────────────────────────────

describe('makeGwId / parseBranchIdFromGwId', () => {
  it('encodes and decodes branch id round-trip', () => {
    const id = makeGwId('b1');
    expect(id).toBe('gw-fork-b1');
    expect(parseBranchIdFromGwId(id)).toBe('b1');
  });

  it('returns undefined for non-gateway ids', () => {
    expect(parseBranchIdFromGwId('s1')).toBeUndefined();
  });
});

// ── buildGatewayNodes ─────────────────────────────────────────────────────────

describe('buildGatewayNodes', () => {
  it('empty branches → no nodes', () => {
    expect(buildGatewayNodes([], [stateA, stateB])).toHaveLength(0);
  });

  it('one branch → one node with correct id, type, draggable', () => {
    const nodes = buildGatewayNodes([branch1], [stateA, stateB, stateC, stateD]);
    expect(nodes).toHaveLength(1);
    expect(nodes[0].id).toBe('gw-fork-b1');
    expect(nodes[0].type).toBe('workflowGateway');
    expect(nodes[0].draggable).toBe(true);
  });

  it('persisted positionX/Y wins over auto-derive', () => {
    const nodes = buildGatewayNodes([branchWithPosition], [stateA, stateB, stateC, stateD]);
    expect(nodes[0].position).toEqual({ x: 300, y: 150 });
  });

  it('session positionMap wins over auto-derive when no persisted pos', () => {
    const session = new Map([['gw-fork-b1', { x: 555, y: 666 }]]);
    const nodes = buildGatewayNodes([branch1], [stateA, stateB, stateC, stateD], session);
    expect(nodes[0].position).toEqual({ x: 555, y: 666 });
  });

  it('persisted position takes priority over session map', () => {
    const session = new Map([['gw-fork-b2', { x: 999, y: 999 }]]);
    const nodes = buildGatewayNodes([branchWithPosition], [stateA, stateB, stateC, stateD], session);
    expect(nodes[0].position).toEqual({ x: 300, y: 150 });
  });

  it('auto-derives deterministic position when positionX/Y null and no session', () => {
    const nodes = buildGatewayNodes([branch1], [stateA, stateB, stateC, stateD]);
    // stateA posX=100,posY=200; children s2(400,200)+s4(400,400) → avg=(400,300)
    // midpoint = ((100+400)/2,(200+300)/2) = (250,250)
    expect(nodes[0].position).toEqual({ x: 250, y: 250 });
  });
});

// ── buildGatewayEdges ─────────────────────────────────────────────────────────

describe('buildGatewayEdges', () => {
  it('empty branches → no edges', () => {
    expect(buildGatewayEdges([], [stateA])).toHaveLength(0);
  });

  it('one branch with 2 children → 5 edges (1 in + 2 fork→child + 2 child→continuation)', () => {
    const edges = buildGatewayEdges([branch1], [stateA, stateB, stateC, stateD]);
    expect(edges).toHaveLength(5);
  });

  it('in-edge connects branchingState to fork node', () => {
    const edges = buildGatewayEdges([branch1], [stateA, stateB, stateC, stateD]);
    const inEdge = edges.find((e) => e.id === 'e-b1-in');
    expect(inEdge?.source).toBe('s1');
    expect(inEdge?.target).toBe('gw-fork-b1');
  });

  it('fork→child edge connects fork to target state', () => {
    const edges = buildGatewayEdges([branch1], [stateA, stateB, stateC, stateD]);
    const e = edges.find((e) => e.id === 'e-b1-c-c1');
    expect(e?.source).toBe('gw-fork-b1');
    expect(e?.target).toBe('s2');
  });

  it('join edge connects child target to continuation state', () => {
    const edges = buildGatewayEdges([branch1], [stateA, stateB, stateC, stateD]);
    const e = edges.find((e) => e.id === 'e-b1-j-c1');
    expect(e?.source).toBe('s2');
    expect(e?.target).toBe('s3');
  });

  it('all edge ids are unique', () => {
    const edges = buildGatewayEdges([branch1], [stateA, stateB, stateC, stateD]);
    const ids = edges.map((e) => e.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('skips branch when branchingState is not in states list', () => {
    const bad = { ...branch1, branchingStateId: 'NONEXISTENT' };
    expect(buildGatewayEdges([bad], [stateA, stateB, stateC, stateD])).toHaveLength(0);
  });
});
