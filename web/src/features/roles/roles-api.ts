// Roles & Permissions API hooks — wraps /api/v1/admin/roles + /permissions (Identity module)

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface RoleDefinitionDto {
  id: string;
  name: string;
  code: string;
  roleType: number; // 1=System, 2=Business
  description?: string;
  isActive: boolean;
  tenantId?: string;
}

export interface CreateRoleRequest {
  name: string;
  code: string;
  description?: string;
  roleType?: number;
}

export interface UpdateRoleRequest {
  name: string;
  description?: string;
}

export interface PermissionDto {
  id: string;
  code: string;
  name: string;
  moduleCode: string;
  resourceCode: string;
  actionCode: string;
  isSensitive: boolean;
}

export interface RolePermissionDto {
  permissionId: string;
  code: string;
  name: string;
}

export interface SyncRolePermissionsResult {
  warnings: string[];
  requiresConfirmation: boolean;
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const rolesQueryKeys = {
  all: ['roles'] as const,
  list: ['roles', 'list'] as const,
  detail: (id: string) => ['roles', 'detail', id] as const,
  permissions: (id: string) => ['roles', 'permissions', id] as const,
};

export const permissionsQueryKeys = {
  all: ['permissions'] as const,
  list: ['permissions', 'list'] as const,
};

// ─── Role Queries ─────────────────────────────────────────────────────────────

/**
 * GET /api/v1/admin/roles — all roles for current tenant.
 * `background: true` marks this as a cosmetic lookup (label resolution) so a 403 is
 * swallowed by the caller instead of triggering the global forbidden/MFA interstitial.
 */
export function useRoles(opts?: { background?: boolean }) {
  return useQuery({
    queryKey: rolesQueryKeys.list,
    queryFn: () =>
      apiClient
        .get<RoleDefinitionDto[]>('/admin/roles', opts?.background ? { suppressAuthInterstitial: true } : undefined)
        .then((r) => r.data),
    staleTime: 10 * 60 * 1000,
    ...(opts?.background ? { meta: { suppressErrorToast: true } } : {}),
  });
}

// ─── Role Mutations ─────────────────────────────────────────────────────────

/** POST /api/v1/admin/roles */
export function useCreateRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateRoleRequest) =>
      apiClient.post<{ id: string }>('/admin/roles', body).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: rolesQueryKeys.all });
    },
  });
}

/** PUT /api/v1/admin/roles/{id} */
export function useUpdateRole(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateRoleRequest) =>
      apiClient.put(`/admin/roles/${id}`, body).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: rolesQueryKeys.all });
    },
  });
}

/** DELETE /api/v1/admin/roles/{id} — soft-delete */
export function useDeleteRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/admin/roles/${id}`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: rolesQueryKeys.all });
    },
  });
}

/** PUT /api/v1/admin/roles/{id}/activate — re-activate */
export function useActivateRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.put(`/admin/roles/${id}/activate`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: rolesQueryKeys.all });
    },
  });
}

// ─── Permission Queries ─────────────────────────────────────────────────────

/**
 * GET /api/v1/admin/permissions — all seeded permissions.
 * `background: true` — see {@link useRoles}: cosmetic lookup, 403 must not redirect.
 */
export function usePermissions(opts?: { background?: boolean }) {
  return useQuery({
    queryKey: permissionsQueryKeys.list,
    queryFn: () =>
      apiClient
        .get<PermissionDto[]>('/admin/permissions', opts?.background ? { suppressAuthInterstitial: true } : undefined)
        .then((r) => r.data),
    staleTime: 10 * 60 * 1000,
    ...(opts?.background ? { meta: { suppressErrorToast: true } } : {}),
  });
}

/** GET /api/v1/admin/roles/{id}/permissions — assigned permissions for a role */
export function useRolePermissions(roleId: string | undefined) {
  return useQuery({
    queryKey: rolesQueryKeys.permissions(roleId ?? ''),
    queryFn: () =>
      apiClient
        .get<RolePermissionDto[]>(`/admin/roles/${roleId}/permissions`)
        .then((r) => r.data),
    enabled: Boolean(roleId),
  });
}

/** PUT /api/v1/admin/roles/{id}/permissions — sync (replace all) */
export function useSyncRolePermissions(roleId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (args: { permissionIds: string[]; confirmed?: boolean; justification?: string }) =>
      apiClient
        .put<SyncRolePermissionsResult>(
          `/admin/roles/${roleId}/permissions`,
          { permissionIds: args.permissionIds, justification: args.justification },
          args.confirmed ? { headers: { 'X-Confirm-SoD': 'true' } } : undefined,
        )
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: rolesQueryKeys.permissions(roleId) });
      queryClient.invalidateQueries({ queryKey: rolesQueryKeys.all });
    },
  });
}
