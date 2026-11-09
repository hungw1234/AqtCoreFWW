import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { PaginatedResult } from '@/shared/types/api';
import type { ExternalIdentityProvider } from '@/features/external-identities/external-identities-api';

// ─── Types ───────────────────────────────────────────────────────────────────

export interface JitProviderConfigDto {
  id: number;
  scheme: string;
  displayName: string;
  providerType: number;
  jitEnabled: boolean;
  defaultRoleName: string;
  requireApproval: boolean;
  claimMappingJson?: string;
  defaultTenantId?: string;
  allowedDomainsJson?: string;
  maxProvisionsPerHour: number;
  isActive: boolean;
}

export interface CreateJitProviderConfigDto {
  scheme: string;
  displayName: string;
  providerType: number;
  jitEnabled: boolean;
  defaultRoleName: string;
  requireApproval: boolean;
  claimMappingJson?: string;
  defaultTenantId?: string;
  allowedDomainsJson?: string;
  maxProvisionsPerHour: number;
}

export interface UpdateJitProviderConfigDto extends CreateJitProviderConfigDto {
  id: number;
}

// Provider type enum values matching BE ExternalIdentityProvider
export const PROVIDER_TYPE_MAP: Record<number, ExternalIdentityProvider> = {
  1: 'SSO',
  2: 'LDAP',
  3: 'VNeID',
  4: 'OAuth',
  5: 'SAML',
};

export const PROVIDER_TYPE_OPTIONS = Object.entries(PROVIDER_TYPE_MAP).map(
  ([value, label]) => ({ value: Number(value), label }),
);

// ─── Query keys ──────────────────────────────────────────────────────────────

export const jitConfigQueryKeys = {
  all: ['jit-provider-configs'] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/jit-provider-configs — returns paginated result */
export function useJitProviderConfigs() {
  return useQuery({
    queryKey: jitConfigQueryKeys.all,
    queryFn: () =>
      apiClient
        .get<PaginatedResult<JitProviderConfigDto>>('/admin/jit-provider-configs', {
          params: { pageSize: 100 },
        })
        .then((r) => r.data),
    placeholderData: (prev) => prev,
  });
}

// ─── Mutations ───────────────────────────────────────────────────────────────

/** POST /api/v1/admin/jit-provider-configs */
export function useCreateJitProviderConfig() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (dto: CreateJitProviderConfigDto) =>
      apiClient.post('/admin/jit-provider-configs', dto).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: jitConfigQueryKeys.all }),
  });
}

/** PUT /api/v1/admin/jit-provider-configs/{id} */
export function useUpdateJitProviderConfig() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...dto }: UpdateJitProviderConfigDto) =>
      apiClient
        .put(`/admin/jit-provider-configs/${id}`, dto)
        .then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: jitConfigQueryKeys.all }),
  });
}

/** DELETE /api/v1/admin/jit-provider-configs/{id} — soft-delete */
export function useDeleteJitProviderConfig() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) =>
      apiClient.delete(`/admin/jit-provider-configs/${id}`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: jitConfigQueryKeys.all }),
  });
}
