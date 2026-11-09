import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type {
  TenantAdminDto,
  TenantDetailDto,
  TenantListParams,
  CreateTenantRequest,
  UpdateTenantRequest,
  ChangeTenantStatusRequest,
  PagedResult,
} from './tenant-types';

// Top-level key root MUST be 'tenants' so the tenant-switch invalidation predicate
// (web/src/core/api/query-config.ts) refetches list/detail on override change.
export const tenantQueryKeys = {
  all: ['tenants'] as const,
  list: (params: TenantListParams) => ['tenants', 'list', params] as const,
  detail: (id: string) => ['tenants', 'detail', id] as const,
};

/** GET /api/v1/admin/tenants — paginated, filterable. */
export function useTenants(params: TenantListParams) {
  return useQuery({
    queryKey: tenantQueryKeys.list(params),
    queryFn: () =>
      apiClient
        .get<PagedResult<TenantAdminDto>>('/admin/tenants', { params })
        .then((r) => r.data),
    placeholderData: (prev) => prev,
  });
}

/** GET /api/v1/admin/tenants/{id} */
export function useTenant(id: string) {
  return useQuery({
    queryKey: tenantQueryKeys.detail(id),
    queryFn: () =>
      apiClient.get<TenantDetailDto>(`/admin/tenants/${id}`).then((r) => r.data),
    enabled: Boolean(id),
  });
}

/** POST /api/v1/admin/tenants — returns { id } envelope on 201. */
export function useCreateTenant() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateTenantRequest) =>
      apiClient.post<{ id: string }>('/admin/tenants', body).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tenantQueryKeys.all });
    },
  });
}

/** PUT /api/v1/admin/tenants/{id} */
export function useUpdateTenant(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateTenantRequest) =>
      apiClient.put<void>(`/admin/tenants/${id}`, body).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tenantQueryKeys.all });
    },
  });
}

/** POST /api/v1/admin/tenants/{id}/status — Active | Suspended | Archived. */
export function useChangeTenantStatus(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: ChangeTenantStatusRequest) =>
      apiClient.post<void>(`/admin/tenants/${id}/status`, body).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tenantQueryKeys.all });
    },
  });
}

/** DELETE /api/v1/admin/tenants/{id} — soft-delete (Status -> Archived). */
export function useArchiveTenant() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete<void>(`/admin/tenants/${id}`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tenantQueryKeys.all });
    },
  });
}
