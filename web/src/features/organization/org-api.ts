import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { OrgUnitDto, CreateOrgUnitRequest, UpdateOrgUnitRequest, StaffAssignmentDto } from './org-types';

// ─── Query keys ──────────────────────────────────────────────────────────────

export const orgQueryKeys = {
  all: ['org-units'] as const,
  list: () => ['org-units', 'list'] as const,
  staff: (unitId: string) => ['org-units', 'staff', unitId] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/org/units — flat list, client builds tree */
export function useOrgUnits() {
  return useQuery({
    queryKey: orgQueryKeys.list(),
    queryFn: () =>
      apiClient.get<OrgUnitDto[]>('/admin/org/units').then((r) => r.data),
  });
}

/** GET /api/v1/admin/org/units/{unitId}/members */
export function useOrgUnitStaff(unitId: string | null) {
  return useQuery({
    queryKey: orgQueryKeys.staff(unitId ?? ''),
    queryFn: () =>
      apiClient
        .get<StaffAssignmentDto[]>(`/admin/org/units/${unitId}/members`)
        .then((r) => r.data),
    enabled: Boolean(unitId),
  });
}

// ─── Mutations ───────────────────────────────────────────────────────────────

/** POST /api/v1/admin/org/units */
export function useCreateOrgUnit() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateOrgUnitRequest) =>
      apiClient.post<OrgUnitDto>('/admin/org/units', body).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: orgQueryKeys.all });
    },
  });
}

/** PUT /api/v1/admin/org/units/{id} */
export function useUpdateOrgUnit(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateOrgUnitRequest) =>
      apiClient.put<OrgUnitDto>(`/admin/org/units/${id}`, body).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: orgQueryKeys.all });
    },
  });
}
