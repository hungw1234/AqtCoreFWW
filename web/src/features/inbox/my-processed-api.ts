// my-processed-api.ts — cursor-paginated hook for "dossiers I have processed" (View 2)
// Endpoint: GET /api/v1/workflow/instances/my-processed?cursor=<base64>&pageSize=<n>
// Wire shape (after apiClientMutator envelope unwrap): PagedResult<MyProcessedInstanceDto>
//   { items, totalCount, meta: { nextCursor, hasNextPage, pageSize, ... } }
// Uses useInfiniteQuery — mirrors use-workflow-instances-infinite.ts pattern.
// entityId in the DTO == BpActiveDto.id (set at run-start as selectedBpId; verified new-bp-run-wizard:174)

import { useInfiniteQuery } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Response DTO ─────────────────────────────────────────────────────────────

/** Mirrors BE MyProcessedInstanceDto — no businessProcessName (resolved client-side from entityId) */
export interface MyProcessedInstanceDto {
  workflowInstancePublicId: string;   // Guid — navigate to /business-process-runs/$id
  currentStateId: number;             // long (bigint) from BE
  currentStateName: string;
  entityType: string;                 // always "BusinessProcess" (server-scoped)
  entityId: string;                   // Guid — matches BpActiveDto.id for name resolution
  startedAt: string;
  completedAt: string | null;
  isFinal: boolean;
}

/** Matches BE PagedResult<T> cursor-mode shape (totalCount null in cursor mode) */
export interface CursorPagedResult<T> {
  items: T[];
  totalCount: number | null;
  meta: {
    nextCursor: string | null;
    hasNextPage: boolean;
    pageSize?: number | null;
  };
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

const MY_PROCESSED_PAGE_SIZE = 20;

/**
 * Cursor-paginated list of BP-Run instances the caller has transitioned at least once.
 * Pages are accumulated (flatMap) — use fetchNextPage() + hasNextPage to load more.
 */
export function useMyProcessedInfinite() {
  return useInfiniteQuery<CursorPagedResult<MyProcessedInstanceDto>, Error>({
    queryKey: ['workflow', 'instances', 'my-processed'],
    queryFn: ({ pageParam }) => {
      const params = new URLSearchParams();
      params.set('pageSize', String(MY_PROCESSED_PAGE_SIZE));
      if (pageParam) params.set('cursor', pageParam as string);
      return apiClient
        .get<CursorPagedResult<MyProcessedInstanceDto>>(
          `/workflow/instances/my-processed?${params}`,
        )
        .then((r) => r.data);
    },
    initialPageParam: null as string | null,
    // Return undefined (not null) when no next page — React Query stops fetching
    getNextPageParam: (last) =>
      last.meta.hasNextPage ? last.meta.nextCursor : undefined,
    staleTime: 30_000,
  });
}
