// Data classification API hooks — React Query wrappers for classification admin endpoints
// Endpoints: GET /api/v1/admin/compliance/classifications
//            GET /api/v1/admin/compliance/classification-rules
//            PUT /api/v1/admin/compliance/classification-rules/{level}
// Permissions: COMPLIANCE.CLASSIFICATION.VIEW (read), COMPLIANCE.CLASSIFICATION.MANAGE (write)

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { ClassificationInventory, ClassificationRule, UpdateClassificationRuleRequest } from './classification-types';

// ─── Query keys ──────────────────────────────────────────────────────────────

export const classificationQueryKeys = {
  all: ['data-classification'] as const,
  inventory: () => ['data-classification', 'inventory'] as const,
  rules: () => ['data-classification', 'rules'] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/compliance/classifications — scan + inventory */
export function useClassificationInventory() {
  return useQuery({
    queryKey: classificationQueryKeys.inventory(),
    queryFn: () =>
      apiClient
        .get<ClassificationInventory>('/admin/compliance/classifications')
        .then((r) => r.data),
    // Inventory is computed from assemblies at request time; no need to aggressively refetch
    staleTime: 5 * 60 * 1000, // 5 min
  });
}

/** GET /api/v1/admin/compliance/classification-rules — 4 level-handling rules */
export function useClassificationRules() {
  return useQuery({
    queryKey: classificationQueryKeys.rules(),
    queryFn: () =>
      apiClient
        .get<ClassificationRule[]>('/admin/compliance/classification-rules')
        .then((r) => r.data),
    staleTime: 5 * 60 * 1000,
  });
}

// ─── Mutations ───────────────────────────────────────────────────────────────

/**
 * PUT /api/v1/admin/compliance/classification-rules/{level}
 * Requires COMPLIANCE.CLASSIFICATION.MANAGE permission.
 * Throws on 409 Conflict (optimistic concurrency — RowVersion mismatch).
 */
export function useUpdateClassificationRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ level, body }: { level: number; body: UpdateClassificationRuleRequest }) =>
      apiClient
        .put(`/admin/compliance/classification-rules/${level}`, body)
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: classificationQueryKeys.all });
    },
  });
}
