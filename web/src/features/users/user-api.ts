import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { PaginatedResult } from '@/shared/types/api';
import type { UserDto, CreateUserRequest, UpdateUserRequest, UserListParams } from './user-types';

// ─── Query keys ──────────────────────────────────────────────────────────────

export const userQueryKeys = {
  all: ['users'] as const,
  list: (params: UserListParams) => ['users', 'list', params] as const,
  detail: (id: string) => ['users', 'detail', id] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/users — paginated, filterable */
export function useUsers(params: UserListParams) {
  return useQuery({
    queryKey: userQueryKeys.list(params),
    queryFn: () =>
      apiClient
        .get<PaginatedResult<UserDto>>('/admin/users', { params })
        .then((r) => r.data),
    placeholderData: (prev) => prev,
  });
}

/** GET /api/v1/admin/users/{id} */
export function useUser(id: string) {
  return useQuery({
    queryKey: userQueryKeys.detail(id),
    queryFn: () =>
      apiClient.get<UserDto>(`/admin/users/${id}`).then((r) => r.data),
    enabled: Boolean(id),
  });
}

// ─── Mutations ───────────────────────────────────────────────────────────────

/** POST /api/v1/admin/users */
export function useCreateUser() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateUserRequest) =>
      apiClient.post<UserDto>('/admin/users', body).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: userQueryKeys.all });
    },
  });
}

/** PUT /api/v1/admin/users/{id} */
export function useUpdateUser(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateUserRequest) =>
      apiClient.put<UserDto>(`/admin/users/${id}`, body).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: userQueryKeys.all });
    },
  });
}

/** DELETE /api/v1/admin/users/{id} */
export function useDeleteUser() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/admin/users/${id}`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: userQueryKeys.all });
    },
  });
}

/** POST /api/v1/admin/users/{id}/roles — assign a single role (additive) */
export function useAssignRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, role }: { id: string; role: string }) =>
      apiClient.post(`/admin/users/${id}/roles`, { role }).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: userQueryKeys.all });
    },
  });
}

/** POST /api/v1/admin/org/staff/{userId}/assign — assign user to org unit */
export function useAssignStaff() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ userId, orgUnitId, roleInOrg }: { userId: string; orgUnitId: string; roleInOrg: string }) =>
      apiClient.post(`/admin/org/staff/${userId}/assign`, { orgUnitId, roleInOrg, positionTitle: roleInOrg }).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: userQueryKeys.all });
    },
  });
}

/** PUT /api/v1/admin/users/{id}/roles — replace all roles (sync) */
export function useSyncRoles() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, roles }: { id: string; roles: string[] }) =>
      apiClient.put(`/admin/users/${id}/roles`, { roles }).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: userQueryKeys.all });
    },
  });
}
