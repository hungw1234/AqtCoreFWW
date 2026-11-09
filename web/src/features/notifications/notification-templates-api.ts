// React Query hooks for the admin notification-templates endpoints
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type {
  NotificationTemplateDto,
  CreateNotificationTemplatePayload,
  UpdateNotificationTemplatePayload,
} from './notification-templates-types';
import type { PagedResult } from './notification-types';

const BASE = '/admin/notification-templates';

export const templateQueryKeys = {
  list: (page: number) => ['notification-templates', 'list', page] as const,
  detail: (id: string) => ['notification-templates', 'detail', id] as const,
};

/** GET /api/v1/admin/notification-templates */
export function useNotificationTemplates(page = 1, pageSize = 20) {
  return useQuery({
    queryKey: templateQueryKeys.list(page),
    queryFn: () =>
      apiClient
        .get<PagedResult<NotificationTemplateDto>>(BASE, { params: { page, pageSize } })
        .then((r) => r.data),
  });
}

/** POST /api/v1/admin/notification-templates */
export function useCreateNotificationTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateNotificationTemplatePayload) =>
      apiClient.post<string>(BASE, payload).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['notification-templates'] }),
  });
}

/** PUT /api/v1/admin/notification-templates/{id} */
export function useUpdateNotificationTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...payload }: { id: string } & UpdateNotificationTemplatePayload) =>
      apiClient.put(`${BASE}/${id}`, payload).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['notification-templates'] }),
  });
}

/** DELETE /api/v1/admin/notification-templates/{id} */
export function useDeleteNotificationTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`${BASE}/${id}`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['notification-templates'] }),
  });
}
