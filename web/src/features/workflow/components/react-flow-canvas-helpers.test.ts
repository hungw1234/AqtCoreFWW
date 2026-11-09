// Unit tests for buildNodes fork-affordance flagging (canFork + onAddFork injection).
// buildNodes is pure; these assert the per-node fork button is offered only on forkable states.

import { describe, it, expect, vi } from 'vitest';
import { buildNodes, type StateNodeData, type ForkNodeOptions } from './react-flow-canvas-helpers';
import type { WorkflowStateDto } from '../workflow-types';

const initial: WorkflowStateDto = {
  id: 's1', name: 'Draft', displayNameVi: 'Nháp', displayNameEn: 'Draft',
  isInitial: true, isFinal: false, color: '#aaa', sortOrder: 0,
};
const middle: WorkflowStateDto = {
  id: 's2', name: 'Review', displayNameVi: 'Xem xét', displayNameEn: 'Review',
  isInitial: false, isFinal: false, color: '#bbb', sortOrder: 1,
};
const final: WorkflowStateDto = {
  id: 's3', name: 'Done', displayNameVi: 'Xong', displayNameEn: 'Done',
  isInitial: false, isFinal: true, color: '#ccc', sortOrder: 2,
};
const states = [initial, middle, final];

const dataOf = (nodes: ReturnType<typeof buildNodes>, id: string) =>
  nodes.find((n) => n.id === id)!.data as unknown as StateNodeData;

describe('buildNodes — fork affordance', () => {
  it('flags non-final, un-forked states as canFork when forkOptions provided', () => {
    const onAddFork = vi.fn();
    const forkOptions: ForkNodeOptions = { branchedStateIds: new Set(), onAddFork };
    const nodes = buildNodes(states, undefined, undefined, forkOptions);

    expect(dataOf(nodes, 's1').canFork).toBe(true);
    expect(dataOf(nodes, 's2').canFork).toBe(true);
  });

  it('does NOT flag final states (RB4 — fork cannot sit on a final state)', () => {
    const forkOptions: ForkNodeOptions = { branchedStateIds: new Set(), onAddFork: vi.fn() };
    const nodes = buildNodes(states, undefined, undefined, forkOptions);
    expect(dataOf(nodes, 's3').canFork).toBe(false);
  });

  it('does NOT flag states that already own a branch (RB3 — one fork per state)', () => {
    const forkOptions: ForkNodeOptions = { branchedStateIds: new Set(['s2']), onAddFork: vi.fn() };
    const nodes = buildNodes(states, undefined, undefined, forkOptions);
    expect(dataOf(nodes, 's2').canFork).toBe(false);
    expect(dataOf(nodes, 's1').canFork).toBe(true);
  });

  it('leaves canFork false + onAddFork undefined when no forkOptions (read-only canvas)', () => {
    const nodes = buildNodes(states);
    expect(dataOf(nodes, 's1').canFork).toBe(false);
    expect(dataOf(nodes, 's1').onAddFork).toBeUndefined();
  });

  it('injects the SAME onAddFork ref into every node (no per-node closures)', () => {
    const onAddFork = vi.fn();
    const forkOptions: ForkNodeOptions = { branchedStateIds: new Set(), onAddFork };
    const nodes = buildNodes(states, undefined, undefined, forkOptions);
    expect(dataOf(nodes, 's1').onAddFork).toBe(onAddFork);
    expect(dataOf(nodes, 's2').onAddFork).toBe(onAddFork);
    expect(dataOf(nodes, 's3').onAddFork).toBe(onAddFork);
  });
});
