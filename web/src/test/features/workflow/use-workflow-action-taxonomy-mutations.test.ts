// TC-FE-WFT-AM-001: useCreateWorkflowActionTaxonomy POSTs to /admin/master-data/workflow-taxonomy/actions
// TC-FE-WFT-AM-002: Create success invalidates the actions query cache
// TC-FE-WFT-AM-003: useUpdateWorkflowActionTaxonomy PUTs to .../actions/{id} with body stripping id
// TC-FE-WFT-AM-004: Update success invalidates the actions query cache
// TC-FE-WFT-AM-005: Update 409 GOV_CONCURRENCY_CONFLICT propagates to onError

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

const mockPost = vi.fn();
const mockPut = vi.fn();

vi.mock('@/core/api', () => ({
  apiClient: {
    get: vi.fn(),
    post: (...args: unknown[]) => mockPost(...args),
    put: (...args: unknown[]) => mockPut(...args),
  },
}));

import {
  useCreateWorkflowActionTaxonomy,
  useUpdateWorkflowActionTaxonomy,
  workflowTaxonomyKeys,
  type CreateWorkflowActionTaxonomyPayload,
  type UpdateWorkflowActionTaxonomyPayload,
} from '@/features/workflow/use-workflow-taxonomies';

function makeWrapperWithClient() {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0 }, mutations: { retry: false } },
  });
  const wrapper = ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
  return { wrapper, qc };
}

describe('useCreateWorkflowActionTaxonomy', () => {
  beforeEach(() => {
    mockPost.mockReset();
    mockPut.mockReset();
  });

  // TC-FE-WFT-AM-001
  it('POSTs payload to /admin/master-data/workflow-taxonomy/actions', async () => {
    mockPost.mockResolvedValueOnce({ data: { id: 'act-1' } });
    const { wrapper } = makeWrapperWithClient();
    const { result } = renderHook(() => useCreateWorkflowActionTaxonomy(), { wrapper });

    const payload: CreateWorkflowActionTaxonomyPayload = {
      code: 'Submit',
      nameVi: 'Nộp',
      nameEn: 'Submit',
      sortOrder: 1,
    };
    await result.current.mutateAsync(payload);

    expect(mockPost).toHaveBeenCalledWith(
      '/admin/master-data/workflow-taxonomy/actions',
      payload,
    );
  });

  // TC-FE-WFT-AM-002
  it('invalidates workflowTaxonomyKeys.actions on success', async () => {
    mockPost.mockResolvedValueOnce({ data: { id: 'x' } });
    const { wrapper, qc } = makeWrapperWithClient();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');

    const { result } = renderHook(() => useCreateWorkflowActionTaxonomy(), { wrapper });
    await result.current.mutateAsync({
      code: 'Foo', nameVi: 'Foo', nameEn: 'Foo', sortOrder: 0,
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: workflowTaxonomyKeys.actions });
  });
});

describe('useUpdateWorkflowActionTaxonomy', () => {
  beforeEach(() => {
    mockPost.mockReset();
    mockPut.mockReset();
  });

  // TC-FE-WFT-AM-003
  it('PUTs payload to .../actions/{id} with id stripped from body', async () => {
    mockPut.mockResolvedValueOnce({ data: undefined });
    const { wrapper } = makeWrapperWithClient();
    const { result } = renderHook(() => useUpdateWorkflowActionTaxonomy(), { wrapper });

    const payload: UpdateWorkflowActionTaxonomyPayload = {
      id: 'act-1',
      nameVi: 'Cập nhật',
      nameEn: 'Updated',
      sortOrder: 10,
      rowVersion: 'AAAAAAAAB/M=',
    };
    await result.current.mutateAsync(payload);

    expect(mockPut).toHaveBeenCalledWith(
      '/admin/master-data/workflow-taxonomy/actions/act-1',
      expect.objectContaining({
        nameVi: 'Cập nhật',
        nameEn: 'Updated',
        sortOrder: 10,
        rowVersion: 'AAAAAAAAB/M=',
      }),
    );
    const sentBody = mockPut.mock.calls[0][1] as Record<string, unknown>;
    expect(sentBody).not.toHaveProperty('id');
  });

  // TC-FE-WFT-AM-004
  it('invalidates workflowTaxonomyKeys.actions on success', async () => {
    mockPut.mockResolvedValueOnce({ data: undefined });
    const { wrapper, qc } = makeWrapperWithClient();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');

    const { result } = renderHook(() => useUpdateWorkflowActionTaxonomy(), { wrapper });
    await result.current.mutateAsync({
      id: 'x', nameVi: 'x', nameEn: 'x', sortOrder: 0, rowVersion: 'z',
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: workflowTaxonomyKeys.actions });
  });

  // TC-FE-WFT-AM-005
  it('propagates 409 GOV_CONCURRENCY_CONFLICT error', async () => {
    const conflict = Object.assign(new Error('Another admin updated this row — reload and retry'), {
      response: { status: 409, data: { errors: [{ code: 'GOV_CONCURRENCY_CONFLICT' }] } },
    });
    mockPut.mockRejectedValueOnce(conflict);

    const { wrapper } = makeWrapperWithClient();
    const { result } = renderHook(() => useUpdateWorkflowActionTaxonomy(), { wrapper });

    await expect(
      result.current.mutateAsync({
        id: 'x', nameVi: 'x', nameEn: 'x', sortOrder: 0, rowVersion: 'stale',
      }),
    ).rejects.toMatchObject({ response: { status: 409 } });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
