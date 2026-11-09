import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface AccessReviewDto {
  id: string;
  userId: string;
  userName?: string;
  resourceType: string;
  resourceId: string;
  permission: string;
  requestedBy: string;
  requestedAt: string;
  reason: string | null;
  status: 'Pending' | 'Approved' | 'Rejected';
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// ─── Query keys ─────────────────────────────────────────────────────────────

export const accessReviewQueryKeys = {
  pending: (page: number, pageSize: number) => ['access-reviews', 'pending', page, pageSize] as const,
};

// ─── Queries ────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/access-reviews/pending?page=&pageSize= — list pending reviews */
export function usePendingAccessReviews(page = 1, pageSize = 20) {
  return useQuery({
    queryKey: accessReviewQueryKeys.pending(page, pageSize),
    queryFn: () =>
      apiClient
        .get<PagedResult<AccessReviewDto>>('/admin/access-reviews/pending', {
          params: { page, pageSize },
        })
        .then((r) => r.data),
  });
}

// ─── Mutations ──────────────────────────────────────────────────────────────

/** POST /api/v1/admin/access-reviews/{id}/approve — approve a review */
export function useApproveAccessReview() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.post(`/admin/access-reviews/${id}/approve`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['access-reviews'] });
    },
  });
}

/** POST /api/v1/admin/access-reviews/{id}/reject — reject a review */
export function useRejectAccessReview() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.post(`/admin/access-reviews/${id}/reject`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['access-reviews'] });
    },
  });
}
