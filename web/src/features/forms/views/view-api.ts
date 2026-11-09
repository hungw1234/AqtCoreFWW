// view-api.ts — React Query hooks for Views CRUD (5 BE endpoints)
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { ViewDefinitionDto, ViewType, CreateViewPayload, UpdateViewPayload } from './view-types';

/** Normalize ViewType from BE int (0-3) to string enum */
const VIEW_TYPE_MAP: Record<number, ViewType> = { 0: 'List', 1: 'Grid', 2: 'Kanban', 3: 'Calendar' };

function normalizeViewType(type: string | number): ViewType {
  if (typeof type === 'number') return VIEW_TYPE_MAP[type] ?? 'List';
  return type as ViewType;
}

function normalizeView(v: ViewDefinitionDto): ViewDefinitionDto {
  return { ...v, type: normalizeViewType(v.type) };
}

export const viewQueryKeys = {
  list: (entityType: string) => ['views', entityType] as const,
  detail: (id: string) => ['views', 'detail', id] as const,
};

/** GET /api/v1/views?entityType=X */
export function useViews(entityType: string) {
  return useQuery({
    queryKey: viewQueryKeys.list(entityType),
    queryFn: () =>
      apiClient
        .get<{ items: ViewDefinitionDto[] }>('/views', {
          params: { entityType, page: 1, pageSize: 50 },
        })
        .then((r) => ({
          ...r.data,
          items: r.data.items.map(normalizeView),
        })),
    enabled: Boolean(entityType),
  });
}

/** GET /api/v1/views/{id} */
export function useView(id: string) {
  return useQuery({
    queryKey: viewQueryKeys.detail(id),
    queryFn: () => apiClient.get<ViewDefinitionDto>(`/views/${id}`).then((r) => normalizeView(r.data)),
    enabled: Boolean(id),
  });
}

/** POST /api/v1/views */
export function useCreateView() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateViewPayload) =>
      apiClient.post<ViewDefinitionDto>('/views', body).then((r) => r.data),
    onSuccess: (_, vars) => {
      qc.invalidateQueries({ queryKey: viewQueryKeys.list(vars.entityType) });
    },
  });
}

/** PUT /api/v1/views/{id} */
export function useUpdateView() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: UpdateViewPayload & { id: string }) =>
      apiClient.put<ViewDefinitionDto>(`/views/${id}`, body).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['views'] });
    },
  });
}

/** DELETE /api/v1/views/{id} */
export function useDeleteView() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.delete(`/views/${id}`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['views'] });
    },
  });
}
