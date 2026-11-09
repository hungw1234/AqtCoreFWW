import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types ───────────────────────────────────────────────────────────────────

export interface DataScopeTypeDto {
  type: string;
  description: string;
}

export interface RoleScopeDto {
  id: string;
  dataScopeTypeId: string;
  scopeField?: string;
  scopeValue?: string;
  priority?: number;
}

export interface CreateRoleScopeDto {
  dataScopeTypeId: string; // GUID of the scope type
  scopeField?: string;
  scopeValue?: string;
  priority?: number;
}

// ─── Query keys ──────────────────────────────────────────────────────────────

export const dataScopesQueryKeys = {
  all: ['data-scopes'] as const,
  types: ['data-scopes', 'types'] as const,
  roleScopes: (roleId: string) => ['data-scopes', 'role', roleId] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/data-scopes/types — list all available scope types */
export function useDataScopeTypes() {
  return useQuery({
    queryKey: dataScopesQueryKeys.types,
    queryFn: () =>
      apiClient
        .get<DataScopeTypeDto[]>('/admin/data-scopes/types')
        .then((r) => r.data),
    staleTime: 10 * 60 * 1000, // scope types are static — cache 10 min
  });
}

/** GET /api/v1/admin/data-scopes/roles/{roleId} — scopes assigned to a role */
export function useRoleScopes(roleId: string | null) {
  return useQuery({
    queryKey: dataScopesQueryKeys.roleScopes(roleId ?? ''),
    queryFn: () =>
      apiClient
        .get<RoleScopeDto[]>(`/admin/data-scopes/roles/${roleId}`)
        .then((r) => r.data),
    enabled: Boolean(roleId),
  });
}

// ─── Mutations ───────────────────────────────────────────────────────────────

/** POST /api/v1/admin/data-scopes/roles/{roleId} — add a scope to a role */
export function useCreateRoleScope(roleId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (dto: CreateRoleScopeDto) =>
      apiClient
        .post<RoleScopeDto>(`/admin/data-scopes/roles/${roleId}`, dto)
        .then((r) => r.data),
    onSuccess: () => {
      if (roleId) {
        queryClient.invalidateQueries({ queryKey: dataScopesQueryKeys.roleScopes(roleId) });
      }
    },
  });
}

/** DELETE /api/v1/admin/data-scopes/roles/{roleId}/{scopeId} — remove a scope from a role */
export function useDeleteRoleScope(roleId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (scopeId: string) =>
      apiClient
        .delete(`/admin/data-scopes/roles/${roleId}/${scopeId}`)
        .then((r) => r.data),
    onSuccess: () => {
      if (roleId) {
        queryClient.invalidateQueries({ queryKey: dataScopesQueryKeys.roleScopes(roleId) });
      }
    },
  });
}
