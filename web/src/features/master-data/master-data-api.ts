import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { ProvinceDto, DistrictDto, WardDto } from './master-data-types';

// ─── Query keys ──────────────────────────────────────────────────────────────

export const masterDataKeys = {
  provinces: ['master-data', 'provinces'] as const,
  districts: (provinceId: string) => ['master-data', 'districts', provinceId] as const,
  wards: (districtId: string) => ['master-data', 'wards', districtId] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/masterdata/provinces */
export function useProvinces() {
  return useQuery({
    queryKey: masterDataKeys.provinces,
    queryFn: () =>
      apiClient.get<ProvinceDto[]>('/masterdata/provinces').then((r) => r.data),
    staleTime: 10 * 60 * 1000, // provinces rarely change — cache 10 min
  });
}

/** GET /api/v1/masterdata/provinces/{provinceCode}/districts */
export function useDistricts(provinceId: string | null) {
  return useQuery({
    queryKey: masterDataKeys.districts(provinceId ?? ''),
    queryFn: () =>
      apiClient
        .get<DistrictDto[]>(`/masterdata/provinces/${provinceId}/districts`)
        .then((r) => r.data),
    enabled: Boolean(provinceId),
    staleTime: 10 * 60 * 1000,
  });
}

/** GET /api/v1/masterdata/districts/{districtCode}/wards — convenience flat endpoint */
export function useWards(districtId: string | null) {
  return useQuery({
    queryKey: masterDataKeys.wards(districtId ?? ''),
    queryFn: () =>
      apiClient
        .get<WardDto[]>(`/masterdata/districts/${districtId}/wards`)
        .then((r) => r.data),
    enabled: Boolean(districtId),
    staleTime: 10 * 60 * 1000,
  });
}

// ─── Admin CUD mutations ──────────────────────────────────────────────────────

const ADMIN_BASE = '/admin/master-data';

export interface UpsertProvincePayload { code: string; nameVi: string; nameEn: string; sortOrder?: number; }
export interface UpsertDistrictPayload { code: string; provinceCode: string; nameVi: string; nameEn: string; sortOrder?: number; }
export interface UpsertWardPayload    { code: string; districtCode: string; nameVi: string; nameEn: string; sortOrder?: number; }

/** POST /api/v1/admin/master-data/provinces */
export function useCreateProvince() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (p: UpsertProvincePayload) =>
      apiClient.post(`${ADMIN_BASE}/provinces`, p).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: masterDataKeys.provinces }),
  });
}

/** PUT /api/v1/admin/master-data/provinces/{code} */
export function useUpdateProvince() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ code, ...body }: UpsertProvincePayload) =>
      apiClient.put(`${ADMIN_BASE}/provinces/${code}`, body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: masterDataKeys.provinces }),
  });
}

/** DELETE /api/v1/admin/master-data/provinces/{code} */
export function useDeleteProvince() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (code: string) =>
      apiClient.delete(`${ADMIN_BASE}/provinces/${code}`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: masterDataKeys.provinces }),
  });
}

/** POST /api/v1/admin/master-data/districts */
export function useCreateDistrict() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (p: UpsertDistrictPayload) =>
      apiClient.post(`${ADMIN_BASE}/districts`, p).then((r) => r.data),
    onSuccess: (_, vars) => qc.invalidateQueries({ queryKey: masterDataKeys.districts(vars.provinceCode) }),
  });
}

/** PUT /api/v1/admin/master-data/districts/{code} */
export function useUpdateDistrict() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ code, ...body }: UpsertDistrictPayload) =>
      apiClient.put(`${ADMIN_BASE}/districts/${code}`, body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['master-data', 'districts'] }),
  });
}

/** DELETE /api/v1/admin/master-data/districts/{code} */
export function useDeleteDistrict() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (code: string) =>
      apiClient.delete(`${ADMIN_BASE}/districts/${code}`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['master-data', 'districts'] }),
  });
}

/** POST /api/v1/admin/master-data/wards */
export function useCreateWard() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (p: UpsertWardPayload) =>
      apiClient.post(`${ADMIN_BASE}/wards`, p).then((r) => r.data),
    onSuccess: (_, vars) => qc.invalidateQueries({ queryKey: masterDataKeys.wards(vars.districtCode) }),
  });
}

/** PUT /api/v1/admin/master-data/wards/{code} */
export function useUpdateWard() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ code, ...body }: UpsertWardPayload) =>
      apiClient.put(`${ADMIN_BASE}/wards/${code}`, body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['master-data', 'wards'] }),
  });
}

/** DELETE /api/v1/admin/master-data/wards/{code} */
export function useDeleteWard() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (code: string) =>
      apiClient.delete(`${ADMIN_BASE}/wards/${code}`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['master-data', 'wards'] }),
  });
}
