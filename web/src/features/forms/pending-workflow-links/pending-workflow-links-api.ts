// pending-workflow-links-api.ts — React Query hooks for the admin "Pending Workflow Links" page.
//
// Consumes WorkflowLinkPendingController (BE Phase 05) at /api/v1/forms/workflow-link-pending.
// apiClient (@/core/api) auto-prefixes /api/v1 and unwraps the response envelope (read r.data).

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { PagedResult } from '../form-types';
import type { PendingWorkflowLinkListItemDto } from './pending-workflow-links-types';

const BASE = '/forms/workflow-link-pending';

export const pendingWorkflowLinkKeys = {
  all: ['forms', 'workflow-link-pending'] as const,
  list: (status: string | undefined, page: number, pageSize: number) =>
    ['forms', 'workflow-link-pending', 'list', status ?? 'all', page, pageSize] as const,
};

/** GET /api/v1/forms/workflow-link-pending?status={s}&page&pageSize (paginated). */
export function usePendingWorkflowLinks(
  status: string | undefined,
  page = 1,
  pageSize = 20,
) {
  return useQuery({
    queryKey: pendingWorkflowLinkKeys.list(status, page, pageSize),
    queryFn: () =>
      apiClient
        .get<PagedResult<PendingWorkflowLinkListItemDto>>(BASE, {
          params: { page, pageSize, ...(status ? { status } : {}) },
        })
        .then((r) => r.data),
  });
}

/**
 * POST /api/v1/forms/workflow-link-pending/{submissionId}/retry — 202 Accepted.
 * Re-arms the row (un-shelves a Shelved row); the Hangfire job performs the actual resolve/create.
 */
export function useRetryWorkflowLinkNow() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (submissionId: string) =>
      apiClient.post(`${BASE}/${submissionId}/retry`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: pendingWorkflowLinkKeys.all });
    },
  });
}

/** POST /api/v1/forms/workflow-link-pending/{submissionId}/shelve — 204, terminalize (dead-letter). */
export function useShelveWorkflowLink() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (submissionId: string) =>
      apiClient.post(`${BASE}/${submissionId}/shelve`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: pendingWorkflowLinkKeys.all });
    },
  });
}
