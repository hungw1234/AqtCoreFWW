// Cursor-paginated infinite query hooks for workflow instances (T5-16).
// RT-05: uses hand-written apiClient.get<T> pattern — NOT orval-generated client.
// RT-04: consumed by web/src/routes/admin-workflow-instances-page.tsx (flat routes dir).

import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type {
  CursorPagedResult,
  WorkflowInstanceListItem,
  WorkflowInstanceSummary,
  InstanceFilter,
  InstanceStatus,
} from './workflow-types';

// Re-export InstanceFilter so route consumers can import from one place
export type { InstanceFilter, InstanceStatus };

// ─── Shared param builder ──────────────────────────────────────────────────────

/** Build URLSearchParams from filter — status 'all' is omitted (server returns all) */
function buildFilterParams(filter: InstanceFilter): URLSearchParams {
  const params = new URLSearchParams();
  if (filter.entityType) params.set('entityType', filter.entityType);
  if (filter.stateId) params.set('stateId', filter.stateId);
  // 'all' is a FE-only sentinel — omit to get unfiltered results from BE
  if (filter.status && filter.status !== 'all') params.set('status', filter.status);
  return params;
}

// ─── useWorkflowInstancesInfinite ─────────────────────────────────────────────

/**
 * Cursor-paginated list of workflow instances.
 *
 * - First fetch: no cursor, returns first 50 items.
 * - Subsequent fetches: pass nextCursor from previous page meta.
 * - Filter change resets the query (new query key invalidates all pages).
 * - staleTime: 30s (list refreshes more aggressively than KPIs).
 */
export function useWorkflowInstancesInfinite(filter: InstanceFilter = {}) {
  return useInfiniteQuery<CursorPagedResult<WorkflowInstanceListItem>, Error>({
    queryKey: ['workflow', 'instances', 'infinite', filter],
    queryFn: ({ pageParam }) => {
      const params = buildFilterParams(filter);
      // pageParam is null on the first page, string cursor on subsequent pages
      if (pageParam) params.set('cursor', pageParam as string);
      params.set('pageSize', '50');
      return apiClient
        .get<CursorPagedResult<WorkflowInstanceListItem>>(`/workflow/instances?${params}`)
        .then((r) => r.data);
    },
    // v5 requirement: initialPageParam must be explicit
    initialPageParam: null as string | null,
    // Return undefined (not null) when no next page — React Query stops fetching
    getNextPageParam: (last) =>
      last.meta.hasNextPage ? last.meta.nextCursor : undefined,
    staleTime: 30_000,
  });
}

// ─── useWorkflowInstancesSummary ──────────────────────────────────────────────

/**
 * KPI summary for workflow instances — independent of the paged window.
 * Longer staleTime (60s) avoids hammering summary endpoint on each page fetch.
 * Filter params match the list hook so KPIs reflect the same scope.
 */
export function useWorkflowInstancesSummary(filter: InstanceFilter = {}) {
  return useQuery({
    queryKey: ['workflow', 'instances', 'summary', filter],
    queryFn: () => {
      const params = buildFilterParams(filter);
      return apiClient
        .get<WorkflowInstanceSummary>(`/workflow/instances/summary?${params}`)
        .then((r) => r.data);
    },
    staleTime: 60_000,
  });
}
