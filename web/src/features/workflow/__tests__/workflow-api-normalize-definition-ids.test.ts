// Guards the API-boundary id coercion that the visual-designer save path depends on.
// The BE serializes long ids as JSON numbers; the designer builds a STRING-keyed
// state map (id → name) and resolves every graph reference through it. Any ref left
// numeric misses the map and leaks a raw id into the name-keyed save payload, which
// the BE rejects with 400 — this is exactly how branch refs broke fork saves.
import { describe, it, expect, vi } from 'vitest';
import { normalizeDefinitionIds } from '../workflow-api';
import { branchToInput } from '../components/workflow-graph-input-utils';
import type { WorkflowDefinitionDto } from '../workflow-types';

vi.mock('@/lib/api-client', () => ({ apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() } }));

// Mirrors the wire shape: every id/ref arrives as a JSON number (cast via unknown —
// the DTO types declare string, which is the discrepancy this normalization bridges).
const wireDefinition = {
  id: 'abc',
  name: 'Fork def',
  states: [
    { id: 30015, name: 'Submitted', autoTransitionOnTimeoutId: null },
    { id: 30016, name: 'UnderReview', autoTransitionOnTimeoutId: null },
    { id: 30018, name: 'Returned', autoTransitionOnTimeoutId: null },
    { id: 30019, name: 'Closed', autoTransitionOnTimeoutId: null },
  ],
  transitions: [{ id: 40001, fromStateId: 30015, toStateId: 30018 }],
  branches: [
    {
      id: 50001,
      branchingStateId: 30015,
      continuationStateId: 30018,
      branchType: 'And',
      allowCancelOnAnyReject: false,
      timeoutMinutes: 0,
      positionX: 600,
      positionY: 72,
      children: [
        {
          id: 60001,
          targetStateId: 30016,
          name: 'review-a',
          displayNameVi: 'Thẩm định A',
          sortOrder: 1,
          resolutions: [{ resolutionType: 'Rejected', resultingStateId: 30019 }],
          assignment: null,
        },
      ],
    },
  ],
} as unknown as WorkflowDefinitionDto;

describe('normalizeDefinitionIds', () => {
  it('coerces every state, transition, and branch ref to string', () => {
    const d = normalizeDefinitionIds(wireDefinition);
    expect(d.states.map((s) => s.id)).toEqual(['30015', '30016', '30018', '30019']);
    expect(d.transitions[0].fromStateId).toBe('30015');
    expect(d.transitions[0].toStateId).toBe('30018');
    const b = d.branches![0];
    expect(b.id).toBe('50001');
    expect(b.branchingStateId).toBe('30015');
    expect(b.continuationStateId).toBe('30018');
    expect(b.children[0].id).toBe('60001');
    expect(b.children[0].targetStateId).toBe('30016');
    expect(b.children[0].resolutions[0].resultingStateId).toBe('30019');
  });

  it('keeps a nullable resolution target null and tolerates a missing branches array', () => {
    const noBranches = normalizeDefinitionIds({ ...wireDefinition, branches: undefined } as unknown as WorkflowDefinitionDto);
    expect(noBranches.branches).toEqual([]);
    const nullRes = normalizeDefinitionIds({
      ...wireDefinition,
      branches: [{ ...(wireDefinition.branches![0] as object), children: [{ ...(wireDefinition.branches![0].children[0] as object), resolutions: [{ resolutionType: 'Rejected', resultingStateId: null }] }] }],
    } as unknown as WorkflowDefinitionDto);
    expect(nullRes.branches![0].children[0].resolutions[0].resultingStateId).toBeNull();
  });

  it('normalized branch refs resolve to NAMES through the save-path state map (regression: numeric refs leaked raw ids)', () => {
    const d = normalizeDefinitionIds(wireDefinition);
    // Exactly how handleSave builds the map: string state ids → names.
    const stateMap = new Map(d.states.map((s) => [s.id, s.name]));
    const input = branchToInput(d.branches![0], stateMap);
    expect(input.branchingStateName).toBe('Submitted');
    expect(input.continuationStateName).toBe('Returned');
    expect(input.children[0].targetStateName).toBe('UnderReview');
    expect(input.children[0].resolutions[0].resultingStateName).toBe('Closed');
    // Drag/auto-layout coordinates pass through untouched.
    expect(input.positionX).toBe(600);
    expect(input.positionY).toBe(72);
  });
});
