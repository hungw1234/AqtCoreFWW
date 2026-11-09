import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface AbacRuleDto {
  id: string;
  resource: string;
  action: string;
  attributeKey: string;
  attributeValue: string;
  effect: 'Allow' | 'Deny';
  tenantId: string | null;
  createdAt: string;
}

export interface CreateAbacRuleDto {
  resource: string;
  action: string;
  attributeKey: string;
  attributeValue: string;
  effect: 'Allow' | 'Deny';
}

// ─── Query keys ─────────────────────────────────────────────────────────────

export const abacQueryKeys = {
  rules: () => ['abac-rules'] as const,
};

// ─── Queries ────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/abac-rules — list all ABAC rules */
export function useAbacRules() {
  return useQuery({
    queryKey: abacQueryKeys.rules(),
    queryFn: () =>
      apiClient
        .get<AbacRuleDto[]>('/admin/abac-rules')
        .then((r) => r.data),
  });
}

// ─── Mutations ──────────────────────────────────────────────────────────────

/** POST /api/v1/admin/abac-rules — create a new ABAC rule */
export function useCreateAbacRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (dto: CreateAbacRuleDto) =>
      apiClient.post<AbacRuleDto>('/admin/abac-rules', dto).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['abac-rules'] });
    },
  });
}

/** PUT /api/v1/admin/abac-rules/{id} — update an ABAC rule */
export function useUpdateAbacRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...dto }: CreateAbacRuleDto & { id: string }) =>
      apiClient.put<AbacRuleDto>(`/admin/abac-rules/${id}`, dto).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['abac-rules'] });
    },
  });
}

/** DELETE /api/v1/admin/abac-rules/{id} — delete an ABAC rule */
export function useDeleteAbacRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/admin/abac-rules/${id}`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['abac-rules'] });
    },
  });
}
