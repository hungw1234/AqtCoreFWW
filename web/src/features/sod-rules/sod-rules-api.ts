import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types ───────────────────────────────────────────────────────────────────

export interface SodRuleDto {
  id: string;
  permissionCodeA: string;
  permissionCodeB: string;
  severity: string;
  description?: string;
  tenantId?: string;
  isActive: boolean;
}

export interface CreateSodRuleDto {
  permissionCodeA: string;
  permissionCodeB: string;
  severity: string;
  description?: string;
}

export interface UpdateSodRuleDto extends CreateSodRuleDto {
  isActive: boolean;
}

// ─── Severity Options ────────────────────────────────────────────────────────
/** Severity levels for SoD rules — Low (info), Medium (warning), High (critical) */
export const SOD_SEVERITY_OPTIONS = [
  { value: 'Low', label: 'sodSeverity.low' },
  { value: 'Medium', label: 'sodSeverity.medium' },
  { value: 'High', label: 'sodSeverity.high' },
] as const;

// ─── Query keys ──────────────────────────────────────────────────────────────

export const sodRulesQueryKeys = {
  all: ['sod-rules'] as const,
  list: ['sod-rules', 'list'] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/sod-rules — list all Segregation of Duties rules */
export function useSodRules() {
  return useQuery({
    queryKey: sodRulesQueryKeys.list,
    queryFn: () =>
      apiClient.get<SodRuleDto[]>('/admin/sod-rules').then((r) => r.data),
  });
}

// ─── Mutations ───────────────────────────────────────────────────────────────

/** POST /api/v1/admin/sod-rules */
export function useCreateSodRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (dto: CreateSodRuleDto) =>
      apiClient.post<SodRuleDto>('/admin/sod-rules', dto).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: sodRulesQueryKeys.all });
    },
  });
}

/** PUT /api/v1/admin/sod-rules/{id} */
export function useUpdateSodRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...dto }: UpdateSodRuleDto & { id: string }) =>
      apiClient.put<SodRuleDto>(`/admin/sod-rules/${id}`, dto).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: sodRulesQueryKeys.all });
    },
  });
}
