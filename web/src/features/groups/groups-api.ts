import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types ───────────────────────────────────────────────────────────────────

// Matches BE ListGroupsQuery.GroupDto
export interface GroupDto {
  id: string;
  code: string;
  name: string;
  description?: string;
  isActive: boolean;
  tenantId?: string;
  createdAtUtc: string;
}

// Matches BE GetGroupByIdQuery.GroupDetailDto — roleIds are GUIDs
export interface GroupDetailDto extends GroupDto {
  memberCount: number;
  roleIds: string[];
  members: { userId: string; fullName: string; email: string }[];
}

export interface CreateGroupDto {
  code: string;      // required — unique group code
  name: string;
  description?: string;
}

// ─── Query keys ──────────────────────────────────────────────────────────────

export const groupsQueryKeys = {
  all: ['groups'] as const,
  list: ['groups', 'list'] as const,
  detail: (id: string) => ['groups', 'detail', id] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/groups — list all user groups */
export function useGroups() {
  return useQuery({
    queryKey: groupsQueryKeys.list,
    queryFn: () =>
      apiClient.get<GroupDto[]>('/admin/groups').then((r) => r.data),
  });
}

/** GET /api/v1/admin/groups/{id} — group detail with members and role GUIDs */
export function useGroup(id: string | null) {
  return useQuery({
    queryKey: groupsQueryKeys.detail(id ?? ''),
    queryFn: () =>
      apiClient.get<GroupDetailDto>(`/admin/groups/${id}`).then((r) => r.data),
    enabled: Boolean(id),
  });
}

// ─── Mutations ───────────────────────────────────────────────────────────────

/** POST /api/v1/admin/groups */
export function useCreateGroup() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (dto: CreateGroupDto) =>
      apiClient.post<{ id: string }>('/admin/groups', dto).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: groupsQueryKeys.all });
    },
  });
}

/** PUT /api/v1/admin/groups/{id} */
export function useUpdateGroup() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...dto }: CreateGroupDto & { id: string }) =>
      apiClient.put<void>(`/admin/groups/${id}`, dto).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: groupsQueryKeys.all });
    },
  });
}

/** DELETE /api/v1/admin/groups/{id} */
export function useDeleteGroup() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete<void>(`/admin/groups/${id}`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: groupsQueryKeys.all });
    },
  });
}

/** POST /api/v1/admin/groups/{id}/members — body: { userId: string (GUID) } */
export function useAddGroupMember() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ groupId, userId }: { groupId: string; userId: string }) =>
      apiClient
        .post(`/admin/groups/${groupId}/members`, { userId })
        .then((r) => r.data),
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({ queryKey: groupsQueryKeys.detail(vars.groupId) });
      queryClient.invalidateQueries({ queryKey: groupsQueryKeys.list });
    },
  });
}

/** DELETE /api/v1/admin/groups/{id}/members/{userId} */
export function useRemoveGroupMember() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ groupId, userId }: { groupId: string; userId: string }) =>
      apiClient
        .delete(`/admin/groups/${groupId}/members/${userId}`)
        .then((r) => r.data),
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({ queryKey: groupsQueryKeys.detail(vars.groupId) });
      queryClient.invalidateQueries({ queryKey: groupsQueryKeys.list });
    },
  });
}

/** POST /api/v1/admin/groups/{id}/roles — body: { roleId: string (GUID) } */
export function useAssignGroupRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ groupId, roleId }: { groupId: string; roleId: string }) =>
      apiClient
        .post(`/admin/groups/${groupId}/roles`, { roleId })
        .then((r) => r.data),
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({ queryKey: groupsQueryKeys.detail(vars.groupId) });
      queryClient.invalidateQueries({ queryKey: groupsQueryKeys.list });
    },
  });
}

/** DELETE /api/v1/admin/groups/{id}/roles/{roleId} — roleId is a GUID */
export function useRemoveGroupRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ groupId, roleId }: { groupId: string; roleId: string }) =>
      apiClient
        .delete(`/admin/groups/${groupId}/roles/${roleId}`)
        .then((r) => r.data),
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({ queryKey: groupsQueryKeys.detail(vars.groupId) });
      queryClient.invalidateQueries({ queryKey: groupsQueryKeys.list });
    },
  });
}
