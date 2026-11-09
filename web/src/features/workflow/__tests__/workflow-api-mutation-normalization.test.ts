// Guards that the definition-returning mutations normalize ids the same way the read query does.
// Without it, a consumer that reads graph refs off a create/clone/save-graph RESPONSE (rather than
// re-fetching via the normalized query) would see numeric branch ids and leak them into the
// name-keyed save payload — the same class of bug the read-path normalization already fixed.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

const mockPost = vi.fn();
const mockPut = vi.fn();

vi.mock('@/core/api', () => ({
  apiClient: {
    get: vi.fn(),
    post: (...args: unknown[]) => mockPost(...args),
    put: (...args: unknown[]) => mockPut(...args),
    delete: vi.fn(),
  },
}));

import {
  useCreateWorkflow,
  useCloneWorkflowDefinition,
  useSaveDefinitionGraph,
  useUpdateWorkflowDefinition,
} from '../workflow-api';

// A definition as it arrives on the wire: every id/ref is a JSON number.
const numericDto = {
  id: 'def-guid',
  name: 'Fork',
  states: [{ id: 30015, name: 'Submitted', autoTransitionOnTimeoutId: 40001 }],
  transitions: [{ id: 40001, fromStateId: 30015, toStateId: 30018 }],
  branches: [
    {
      id: 50001,
      branchingStateId: 30015,
      continuationStateId: 30018,
      branchType: 'And',
      allowCancelOnAnyReject: false,
      timeoutMinutes: 0,
      children: [
        {
          id: 60001,
          targetStateId: 30016,
          name: 'review-a',
          displayNameVi: 'A',
          sortOrder: 1,
          resolutions: [{ resolutionType: 'Rejected', resultingStateId: 30019 }],
          assignment: null,
        },
      ],
    },
  ],
};

function wrapper() {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
}

function expectAllStringIds(d: typeof numericDto) {
  // The branch family is the part the read-path fix missed first — assert it specifically.
  const b = d.branches[0];
  expect(typeof b.id).toBe('string');
  expect(typeof b.branchingStateId).toBe('string');
  expect(typeof b.continuationStateId).toBe('string');
  expect(typeof b.children[0].targetStateId).toBe('string');
  expect(typeof b.children[0].resolutions[0].resultingStateId).toBe('string');
  expect(typeof d.states[0].id).toBe('string');
  expect(typeof d.transitions[0].fromStateId).toBe('string');
}

describe('definition-returning mutations normalize ids', () => {
  beforeEach(() => {
    mockPost.mockReset();
    mockPut.mockReset();
  });

  it('useCreateWorkflow normalizes the create response', async () => {
    mockPost.mockResolvedValueOnce({ data: structuredClone(numericDto) });
    const { result } = renderHook(() => useCreateWorkflow(), { wrapper: wrapper() });
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const dto = await result.current.mutateAsync({} as any);
    expectAllStringIds(dto as unknown as typeof numericDto);
  });

  it('useCloneWorkflowDefinition normalizes the clone response', async () => {
    mockPost.mockResolvedValueOnce({ data: structuredClone(numericDto) });
    const { result } = renderHook(() => useCloneWorkflowDefinition('def-guid'), { wrapper: wrapper() });
    const dto = await result.current.mutateAsync();
    expectAllStringIds(dto as unknown as typeof numericDto);
  });

  it('useSaveDefinitionGraph normalizes the save response', async () => {
    mockPut.mockResolvedValueOnce({ data: structuredClone(numericDto) });
    const { result } = renderHook(() => useSaveDefinitionGraph('def-guid'), { wrapper: wrapper() });
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const dto = await result.current.mutateAsync({} as any);
    expectAllStringIds(dto as unknown as typeof numericDto);
  });

  it('useUpdateWorkflowDefinition normalizes the metadata-update response', async () => {
    // Update returns a full DTO via the same MapToDto as create (numeric ids), not a bare ack.
    mockPut.mockResolvedValueOnce({ data: structuredClone(numericDto) });
    const { result } = renderHook(() => useUpdateWorkflowDefinition('def-guid'), { wrapper: wrapper() });
    const dto = await result.current.mutateAsync({ name: 'x', description: 'y' });
    expectAllStringIds(dto as unknown as typeof numericDto);
  });
});
