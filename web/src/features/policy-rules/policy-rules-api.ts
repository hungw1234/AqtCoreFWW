import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types ───────────────────────────────────────────────────────────────────

export interface PolicyRuleDto {
  id: string;
  code: string;
  permissionCode: string;
  conditionExpression?: string;
  effect: 'Allow' | 'Deny';
  priority: number;
  logOnDeny: boolean;
  description?: string;
  tenantId?: string;
  isActive: boolean;
}

export interface CreatePolicyRuleDto {
  code: string;
  permissionCode: string;
  conditionExpression?: string;
  effect: 'Allow' | 'Deny';
  priority: number;
  logOnDeny: boolean;
  description?: string;
}

// UpdatePolicyRuleDto = CreatePolicyRuleDto + isActive toggle
export interface UpdatePolicyRuleDto extends CreatePolicyRuleDto {
  isActive: boolean;
}

// ─── Query keys ──────────────────────────────────────────────────────────────

export const policyRulesQueryKeys = {
  all: ['policy-rules'] as const,
  list: ['policy-rules', 'list'] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/policy-rules — list all access policy rules */
export function usePolicyRules() {
  return useQuery({
    queryKey: policyRulesQueryKeys.list,
    queryFn: () =>
      apiClient.get<PolicyRuleDto[]>('/admin/policy-rules').then((r) => r.data),
  });
}

// ─── Mutations ───────────────────────────────────────────────────────────────

/** POST /api/v1/admin/policy-rules */
export function useCreatePolicyRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (dto: CreatePolicyRuleDto) =>
      apiClient.post<PolicyRuleDto>('/admin/policy-rules', dto).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: policyRulesQueryKeys.all });
    },
  });
}

/** PUT /api/v1/admin/policy-rules/{id} */
export function useUpdatePolicyRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...dto }: UpdatePolicyRuleDto & { id: string }) =>
      apiClient.put<PolicyRuleDto>(`/admin/policy-rules/${id}`, dto).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: policyRulesQueryKeys.all });
    },
  });
}

/** DELETE /api/v1/admin/policy-rules/{id} */
export function useDeletePolicyRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/admin/policy-rules/${id}`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: policyRulesQueryKeys.all });
    },
  });
}
