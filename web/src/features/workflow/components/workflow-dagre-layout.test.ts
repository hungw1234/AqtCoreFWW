// Vitest unit tests for workflow-dagre-layout.ts (computeDagreLayout).
// Dagre is deterministic for a given graph, so we assert STRUCTURAL properties (left→right rank
// ordering, parallel children laned onto distinct rows, gateway ranked between source + children)
// rather than exact pixel coordinates — those are an implementation detail of dagre's spacing.

import { describe, it, expect } from 'vitest';
import { computeDagreLayout } from './workflow-dagre-layout';
import { makeGwId } from './gateway-node-build-utils';
import type { WorkflowStateDto, WorkflowTransitionDto, WorkflowBranchDto } from '../workflow-types';

function state(id: string, name: string, opts: Partial<WorkflowStateDto> = {}): WorkflowStateDto {
  return {
    id, name, displayNameVi: name, displayNameEn: name,
    isInitial: false, isFinal: false, color: '#888', sortOrder: 0,
    positionX: null, positionY: null, ...opts,
  };
}

function transition(id: string, from: string, to: string): WorkflowTransitionDto {
  return {
    id, fromStateId: from, toStateId: to,
    actionName: `a-${id}`, actionLabelVi: id, actionLabelEn: id,
    conditionsJson: null, sortOrder: 0, isRecall: false,
    sourceHandle: null, targetHandle: null, assignment: null,
  } as WorkflowTransitionDto;
}

// Draft → Submitted → (AND fork: UnderReview ∥ Assigned) → Returned → Closed
const states: WorkflowStateDto[] = [
  state('s-draft', 'Draft', { isInitial: true }),
  state('s-sub', 'Submitted'),
  state('s-rev', 'UnderReview'),
  state('s-asn', 'Assigned'),
  state('s-ret', 'Returned'),
  state('s-closed', 'Closed', { isFinal: true }),
];

const transitions: WorkflowTransitionDto[] = [
  transition('t1', 's-draft', 's-sub'),
  transition('t2', 's-ret', 's-closed'),
];

const branch: WorkflowBranchDto = {
  id: 'b1', branchingStateId: 's-sub', continuationStateId: 's-ret',
  branchType: 'And', allowCancelOnAnyReject: false, timeoutMinutes: 0,
  positionX: null, positionY: null,
  children: [
    { id: 'c1', targetStateId: 's-rev', name: 'c1', displayNameVi: 'A', sortOrder: 0, resolutions: [] },
    { id: 'c2', targetStateId: 's-asn', name: 'c2', displayNameVi: 'B', sortOrder: 1, resolutions: [] },
  ],
};

describe('computeDagreLayout', () => {
  it('returns a position for every state and for the fork gateway', () => {
    const { statePositions, gatewayPositions } = computeDagreLayout(states, transitions, [branch]);
    expect(statePositions.size).toBe(states.length);
    states.forEach((s) => expect(statePositions.has(s.id)).toBe(true));
    expect(gatewayPositions.has('b1')).toBe(true);
    // gatewayPositions is keyed by branch id, NOT the gw-fork- node id.
    expect(gatewayPositions.has(makeGwId('b1'))).toBe(false);
  });

  it('lays states out left→right (initial leftmost, final rightmost)', () => {
    const { statePositions } = computeDagreLayout(states, transitions, [branch]);
    const x = (id: string) => statePositions.get(id)!.x;
    expect(x('s-draft')).toBeLessThan(x('s-sub'));
    expect(x('s-sub')).toBeLessThan(x('s-ret'));
    expect(x('s-ret')).toBeLessThan(x('s-closed'));
    expect(x('s-draft')).toBeLessThan(x('s-closed'));
  });

  it('separates the two AND-fork children onto distinct vertical lanes', () => {
    const { statePositions } = computeDagreLayout(states, transitions, [branch]);
    const yRev = statePositions.get('s-rev')!.y;
    const yAsn = statePositions.get('s-asn')!.y;
    // Siblings sharing a rank must not stack on top of each other.
    expect(Math.abs(yRev - yAsn)).toBeGreaterThan(0);
  });

  it('ranks the fork gateway between its branching state and its children', () => {
    const { statePositions, gatewayPositions } = computeDagreLayout(states, transitions, [branch]);
    const gwX = gatewayPositions.get('b1')!.x;
    expect(gwX).toBeGreaterThan(statePositions.get('s-sub')!.x);
    expect(gwX).toBeLessThan(statePositions.get('s-rev')!.x);
  });

  it('does not throw on a recall (cyclic) transition — dagre breaks the cycle internally', () => {
    const cyclic = [...transitions, transition('t-recall', 's-ret', 's-sub')];
    expect(() => computeDagreLayout(states, cyclic, [branch])).not.toThrow();
  });

  it('handles an empty graph (no states) without error', () => {
    const { statePositions, gatewayPositions } = computeDagreLayout([], [], []);
    expect(statePositions.size).toBe(0);
    expect(gatewayPositions.size).toBe(0);
  });
});
