import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { IpRuleDto, CreateIpRuleRequest, UpdateIpRuleRequest } from './ip-rule-types';

// ─── Query keys ──────────────────────────────────────────────────────────────

export const ipRuleQueryKeys = {
  all: ['ip-rules'] as const,
  list: ['ip-rules', 'list'] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/security/ip-rules — list all IP filter rules */
export function useIpRules() {
  return useQuery({
    queryKey: ipRuleQueryKeys.list,
    queryFn: () =>
      apiClient
        .get<IpRuleDto[]>('/admin/security/ip-rules')
        .then((r) => r.data),
  });
}

// ─── Mutations ───────────────────────────────────────────────────────────────

/** POST /api/v1/admin/security/ip-rules — create a new IP filter rule */
export function useCreateIpRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (dto: CreateIpRuleRequest) =>
      apiClient
        .post<number>('/admin/security/ip-rules', dto)
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ipRuleQueryKeys.all });
    },
  });
}

/** PUT /api/v1/admin/security/ip-rules/{id} — update an existing rule (204 No Content) */
export function useUpdateIpRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...dto }: UpdateIpRuleRequest & { id: number }) =>
      apiClient
        .put(`/admin/security/ip-rules/${id}`, dto)
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ipRuleQueryKeys.all });
    },
  });
}

/** DELETE /api/v1/admin/security/ip-rules/{id} — soft-delete a rule (204 No Content) */
export function useDeleteIpRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) =>
      apiClient.delete(`/admin/security/ip-rules/${id}`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ipRuleQueryKeys.all });
    },
  });
}
