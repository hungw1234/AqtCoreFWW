// TC-FE-WFT-DR-001: useDeleteWorkflowStateTaxonomy DELETEs with body {confirmCode, reason}
// TC-FE-WFT-DR-002: Delete success invalidates both active + archived query keys
// TC-FE-WFT-DR-003: Delete 400 GOV_CONFIRM_CODE_MISMATCH propagates
// TC-FE-WFT-DR-004: useRestoreWorkflowStateTaxonomy POSTs to /states/{id}/restore
// TC-FE-WFT-DR-005: Restore 409 GOV_ALREADY_ACTIVE propagates
// TC-FE-WFT-DR-006: useDeleteWorkflowActionTaxonomy DELETEs actions endpoint
// TC-FE-WFT-DR-007: useRestoreWorkflowActionTaxonomy POSTs to actions restore
// TC-FE-WFT-DR-008: Archive hooks call includeInactive=true AND filter isActive=false client-side

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

const mockGet = vi.fn();
const mockPost = vi.fn();
const mockDelete = vi.fn();

vi.mock('@/core/api', () => ({
  apiClient: {
    get: (...args: unknown[]) => mockGet(...args),
    post: (...args: unknown[]) => mockPost(...args),
    put: vi.fn(),
    delete: (...args: unknown[]) => mockDelete(...args),
  },
}));

import {
  useDeleteWorkflowStateTaxonomy,
  useRestoreWorkflowStateTaxonomy,
  useDeleteWorkflowActionTaxonomy,
  useRestoreWorkflowActionTaxonomy,
  useArchivedWorkflowStateTaxonomies,
  useArchivedWorkflowActionTaxonomies,
  workflowTaxonomyKeys,
  workflowTaxonomyArchivedKeys,
  type WorkflowStateTaxonomyDto,
} from '@/features/workflow/use-workflow-taxonomies';

function makeWrapperWithClient() {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0 }, mutations: { retry: false } },
  });
  const wrapper = ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: qc }, children);
  return { wrapper, qc };
}

describe('useDeleteWorkflowStateTaxonomy', () => {
  beforeEach(() => {
    mockGet.mockReset();
    mockPost.mockReset();
    mockDelete.mockReset();
  });

  // TC-FE-WFT-DR-001
  it('DELETEs with body {confirmCode, reason} and strips id', async () => {
    mockDelete.mockResolvedValueOnce({ data: { referencedByDefinitionCount: 0 } });
    const { wrapper } = makeWrapperWithClient();
    const { result } = renderHook(() => useDeleteWorkflowStateTaxonomy(), { wrapper });

    await result.current.mutateAsync({
      id: 'state-1',
      confirmCode: 'Draft',
      reason: 'Replaced by new taxonomy',
    });

    expect(mockDelete).toHaveBeenCalledWith(
      '/admin/master-data/workflow-taxonomy/states/state-1',
      expect.objectContaining({
        data: expect.objectContaining({
          confirmCode: 'Draft',
          reason: 'Replaced by new taxonomy',
        }),
      }),
    );
    // id must not leak into body
    const sentBody = (mockDelete.mock.calls[0][1] as { data: Record<string, unknown> }).data;
    expect(sentBody).not.toHaveProperty('id');
  });

  // TC-FE-WFT-DR-002
  it('invalidates BOTH active and archived query keys on success', async () => {
    mockDelete.mockResolvedValueOnce({ data: { referencedByDefinitionCount: 0 } });
    const { wrapper, qc } = makeWrapperWithClient();
    const invalidateSpy = vi.spyOn(qc, 'invalidateQueries');

    const { result } = renderHook(() => useDeleteWorkflowStateTaxonomy(), { wrapper });
    await result.current.mutateAsync({ id: 'x', confirmCode: 'x', reason: '1234567890' });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: workflowTaxonomyKeys.states });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: workflowTaxonomyArchivedKeys.states });
  });

  // TC-FE-WFT-DR-003
  it('propagates 400 GOV_CONFIRM_CODE_MISMATCH', async () => {
    const err = Object.assign(new Error('confirmCode mismatch'), {
      response: { status: 400, data: { errors: [{ code: 'GOV_CONFIRM_CODE_MISMATCH' }] } },
    });
    mockDelete.mockRejectedValueOnce(err);

    const { wrapper } = makeWrapperWithClient();
    const { result } = renderHook(() => useDeleteWorkflowStateTaxonomy(), { wrapper });

    await expect(
      result.current.mutateAsync({ id: 'x', confirmCode: 'wrong', reason: '1234567890' }),
    ).rejects.toMatchObject({ response: { status: 400 } });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

describe('useRestoreWorkflowStateTaxonomy', () => {
  beforeEach(() => {
    mockPost.mockReset();
  });

  // TC-FE-WFT-DR-004
  it('POSTs to /states/{id}/restore with no body', async () => {
    mockPost.mockResolvedValueOnce({ data: undefined });
    const { wrapper } = makeWrapperWithClient();
    const { result } = renderHook(() => useRestoreWorkflowStateTaxonomy(), { wrapper });

    await result.current.mutateAsync('state-1');

    expect(mockPost).toHaveBeenCalledWith('/admin/master-data/workflow-taxonomy/states/state-1/restore');
  });

  // TC-FE-WFT-DR-005
  it('propagates 409 GOV_ALREADY_ACTIVE', async () => {
    const err = Object.assign(new Error('already active'), {
      response: { status: 409, data: { errors: [{ code: 'GOV_ALREADY_ACTIVE' }] } },
    });
    mockPost.mockRejectedValueOnce(err);

    const { wrapper } = makeWrapperWithClient();
    const { result } = renderHook(() => useRestoreWorkflowStateTaxonomy(), { wrapper });

    await expect(result.current.mutateAsync('x')).rejects.toMatchObject({
      response: { status: 409 },
    });
  });
});

describe('useDeleteWorkflowActionTaxonomy', () => {
  beforeEach(() => mockDelete.mockReset());

  // TC-FE-WFT-DR-006
  it('DELETEs actions endpoint with body', async () => {
    mockDelete.mockResolvedValueOnce({ data: { referencedByDefinitionCount: 0 } });
    const { wrapper } = makeWrapperWithClient();
    const { result } = renderHook(() => useDeleteWorkflowActionTaxonomy(), { wrapper });

    await result.current.mutateAsync({
      id: 'act-1',
      confirmCode: 'Submit',
      reason: '1234567890',
    });

    expect(mockDelete).toHaveBeenCalledWith(
      '/admin/master-data/workflow-taxonomy/actions/act-1',
      expect.objectContaining({ data: expect.objectContaining({ confirmCode: 'Submit' }) }),
    );
  });
});

describe('useRestoreWorkflowActionTaxonomy', () => {
  beforeEach(() => mockPost.mockReset());

  // TC-FE-WFT-DR-007
  it('POSTs to /actions/{id}/restore', async () => {
    mockPost.mockResolvedValueOnce({ data: undefined });
    const { wrapper } = makeWrapperWithClient();
    const { result } = renderHook(() => useRestoreWorkflowActionTaxonomy(), { wrapper });

    await result.current.mutateAsync('act-1');

    expect(mockPost).toHaveBeenCalledWith('/admin/master-data/workflow-taxonomy/actions/act-1/restore');
  });
});

describe('Archived query hooks', () => {
  beforeEach(() => mockGet.mockReset());

  // TC-FE-WFT-DR-008a: states archive filters isActive=false client-side
  it('useArchivedWorkflowStateTaxonomies calls includeInactive=true then filters isActive=false', async () => {
    const active = { id: 'a', code: 'Draft', nameVi: 'Nháp', nameEn: 'Draft', i18nKey: null, icon: null, color: null, category: 'Initial', sortOrder: 1, isActive: true, deprecatedReason: null, rowVersion: 'x' } as WorkflowStateTaxonomyDto;
    const archived = { ...active, id: 'b', code: 'OldCode', isActive: false, deprecatedReason: 'retired' } as WorkflowStateTaxonomyDto;
    mockGet.mockResolvedValueOnce({ data: [active, archived] });

    const { wrapper } = makeWrapperWithClient();
    const { result } = renderHook(() => useArchivedWorkflowStateTaxonomies(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockGet).toHaveBeenCalledWith(
      '/admin/master-data/workflow-taxonomy/states',
      expect.objectContaining({ params: { includeInactive: true } }),
    );
    expect(result.current.data).toHaveLength(1);
    expect(result.current.data?.[0].code).toBe('OldCode');
  });

  // TC-FE-WFT-DR-008b
  it('useArchivedWorkflowActionTaxonomies mirrors the states filter pattern', async () => {
    mockGet.mockResolvedValueOnce({ data: [] });

    const { wrapper } = makeWrapperWithClient();
    const { result } = renderHook(() => useArchivedWorkflowActionTaxonomies(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockGet).toHaveBeenCalledWith(
      '/admin/master-data/workflow-taxonomy/actions',
      expect.objectContaining({ params: { includeInactive: true } }),
    );
  });
});
