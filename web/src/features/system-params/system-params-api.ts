import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type {
  SystemParamDto,
  UpdateSystemParamRequest,
  FeatureFlagDto,
  UpdateFeatureFlagRequest,
  AnnouncementDto,
  CreateAnnouncementRequest,
} from './system-params-types';

// ─── Query keys ──────────────────────────────────────────────────────────────

export const sysParamKeys = {
  params: ['system-params'] as const,
  flags: ['feature-flags'] as const,
  announcements: ['announcements'] as const,
};

// ─── System Params ───────────────────────────────────────────────────────────

/** GET /api/v1/admin/system-params */
export function useSystemParams() {
  return useQuery({
    queryKey: sysParamKeys.params,
    queryFn: () =>
      apiClient.get<SystemParamDto[]>('/admin/system-params').then((r) => r.data),
  });
}

/** PUT /api/v1/admin/system-params/{key} */
export function useUpdateSystemParam() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ key, body }: { key: string; body: UpdateSystemParamRequest }) =>
      apiClient.put<SystemParamDto>(`/admin/system-params/${key}`, body).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: sysParamKeys.params });
    },
  });
}

// ─── Feature Flags ───────────────────────────────────────────────────────────

/** GET /api/v1/admin/feature-flags */
export function useFeatureFlags() {
  return useQuery({
    queryKey: sysParamKeys.flags,
    queryFn: () =>
      apiClient.get<FeatureFlagDto[]>('/admin/feature-flags').then((r) => r.data),
  });
}

/** PUT /api/v1/admin/feature-flags/{name} */
export function useUpdateFeatureFlag() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateFeatureFlagRequest }) =>
      apiClient.put<FeatureFlagDto>(`/admin/feature-flags/${id}`, body).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: sysParamKeys.flags });
    },
  });
}

// ─── Announcements ───────────────────────────────────────────────────────────

/** GET /api/v1/admin/announcements — admin list all */
export function useAnnouncements() {
  return useQuery({
    queryKey: sysParamKeys.announcements,
    queryFn: () =>
      apiClient.get<AnnouncementDto[]>('/admin/announcements').then((r) => r.data),
  });
}

/** POST /api/v1/admin/announcements */
export function useCreateAnnouncement() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateAnnouncementRequest) =>
      apiClient.post<AnnouncementDto>('/admin/announcements', body).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: sysParamKeys.announcements });
    },
  });
}

/** DELETE /api/v1/admin/announcements/{id} */
export function useDeleteAnnouncement() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/admin/announcements/${id}`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: sysParamKeys.announcements });
    },
  });
}
