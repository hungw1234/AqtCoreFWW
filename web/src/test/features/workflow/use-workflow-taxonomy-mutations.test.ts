// TC-FE-WFT-M-001: useCreateWorkflowStateTaxonomy POSTs to /admin/master-data/workflow-taxonomy/states
// TC-FE-WFT-M-002: Create success invalidates the states query cache
// TC-FE-WFT-M-003: useUpdateWorkflowStateTaxonomy PUTs to /admin/master-data/workflow-taxonomy/states/{id}
// TC-FE-WFT-M-004: Update success invalidates the states query cache
// TC-FE-WFT-M-005: Update 409 GOV_CONCURRENCY_CONFLICT surfaces onError with raw error

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// ─── Mock apiClient ──────────────────────────────────────────────────────────

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
  useCreateWorkflowStateTaxonomy,
  useUpdateWorkflowStateTaxonomy,
  workflowTaxonomyKeys,
  type CreateWorkflowStateTaxonomyPayload,
  type UpdateWorkflowStateTaxonomyPayload,
} from '@/features/workflow/use-workflow-taxonomies';

// ─── Wrapper factory ─────────────────────────────────────────────────────────

function makeWrapperWithClient() {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0 }, mutations: { retry: false } },
  });
  const wrapper = ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
  return { wrapper, qc };
}

// ─── Tests ───────────────────────────────────────────────────────────────────

describe('useCreateWorkflowStateTaxonomy', () => {
  beforeEach(() => {
    mockPost.mockReset();
    mockPut.mockReset();
  });

  // TC-FE-WFT-M-001
  it('POSTs payload to /admin/master-data/workflow-taxonomy/states', async () => {
    mockPost.mockResolvedValueOnce({ data: { id: 'abc-123' } });
    const { wrapper } = makeWrapperWithClient();

    const { result } = renderHook(() => useCreateWorkflowStateTaxonomy(), { wrapper });

    const payload: CreateWorkflowStateTaxonomyPayload = {
      code: 'InReview',
      nameVi: 'Đang xử lý',
      nameEn: 'In Review',
      category: 'Intermediate',
      sortOrder: 5,
    };
    await result.current.mutateAsync(payload);

    expect(mockPost).toHaveBeenCalledWith(
      '/admin/master-data/workflow-taxonomy/states',
      payload,
    );
  });

  // TC-FE-WFT-M-002
  it('invalidates workflowTaxonomyKeys.states on success', async () => {
    mockPost.mockResolvedValueOnce({ data: { id: 'abc-123' } });
    const { wrapper, qc } = makeWrapperWithClient();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');

    const { result } = renderHook(() => useCreateWorkflowStateTaxonomy(), { wrapper });
    await result.current.mutateAsync({
      code: 'Foo', nameVi: 'Foo', nameEn: 'Foo', category: 'Initial', sortOrder: 0,
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: workflowTaxonomyKeys.states });
  });
});

describe('useUpdateWorkflowStateTaxonomy', () => {
  beforeEach(() => {
    mockPost.mockReset();
    mockPut.mockReset();
  });

  // TC-FE-WFT-M-003
  it('PUTs payload to /admin/master-data/workflow-taxonomy/states/{id}', async () => {
    mockPut.mockResolvedValueOnce({ data: undefined });
    const { wrapper } = makeWrapperWithClient();

    const { result } = renderHook(() => useUpdateWorkflowStateTaxonomy(), { wrapper });

    const payload: UpdateWorkflowStateTaxonomyPayload = {
      id: 'abc-123',
      nameVi: 'Updated',
      nameEn: 'Updated',
      category: 'Intermediate',
      sortOrder: 10,
      rowVersion: 'AAAAAAAAB/E=',
    };
    await result.current.mutateAsync(payload);

    // Hook must strip `id` from the body (it goes in the URL)
    expect(mockPut).toHaveBeenCalledWith(
      '/admin/master-data/workflow-taxonomy/states/abc-123',
      expect.objectContaining({
        nameVi: 'Updated',
        nameEn: 'Updated',
        category: 'Intermediate',
        sortOrder: 10,
        rowVersion: 'AAAAAAAAB/E=',
      }),
    );
    // Ensure id NOT in the body
    const sentBody = mockPut.mock.calls[0][1] as Record<string, unknown>;
    expect(sentBody).not.toHaveProperty('id');
  });

  // TC-FE-WFT-M-004
  it('invalidates workflowTaxonomyKeys.states on success', async () => {
    mockPut.mockResolvedValueOnce({ data: undefined });
    const { wrapper, qc } = makeWrapperWithClient();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');

    const { result } = renderHook(() => useUpdateWorkflowStateTaxonomy(), { wrapper });
    await result.current.mutateAsync({
      id: 'x', nameVi: 'x', nameEn: 'x', category: 'Initial', sortOrder: 0, rowVersion: 'z',
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: workflowTaxonomyKeys.states });
  });

  // TC-FE-WFT-M-005
  it('propagates 409 GOV_CONCURRENCY_CONFLICT error', async () => {
    const conflict = Object.assign(new Error('Another admin updated this row — reload and retry'), {
      response: { status: 409, data: { errors: [{ code: 'GOV_CONCURRENCY_CONFLICT' }] } },
    });
    mockPut.mockRejectedValueOnce(conflict);

    const { wrapper } = makeWrapperWithClient();
    const { result } = renderHook(() => useUpdateWorkflowStateTaxonomy(), { wrapper });

    await expect(
      result.current.mutateAsync({
        id: 'x', nameVi: 'x', nameEn: 'x', category: 'Initial', sortOrder: 0, rowVersion: 'stale',
      }),
    ).rejects.toMatchObject({
      response: { status: 409 },
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
