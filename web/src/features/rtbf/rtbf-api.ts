// RTBF API hooks — React Query wrappers for Right to Be Forgotten admin endpoints

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface RtbfRequestDto {
  id: string;
  tenantId: string;
  dataSubjectId: string;
  dataSubjectEmail?: string;
  citizenNationalId?: string;
  status: string;
  requestedAt: string;
  dueBy: string;
  processedBy?: string;
  processedAt?: string;
  rejectionReason?: string;
}

// BE CreateRtbfBody: { subjectEmail: string, reason?: string }
export interface CreateRtbfRequest {
  subjectEmail: string;
  reason?: string;
}

// ─── Query Keys ─────────────────────────────────────────────────────────────

export const rtbfQueryKeys = {
  list: (params?: { status?: string; page?: number; pageSize?: number }) =>
    ['rtbf-requests', params] as const,
};

// ─── Queries ────────────────────────────────────────────────────────────────

export interface RtbfListParams {
  status?: string;
  page: number;
  pageSize: number;
  search?: string;
}

/** GET /api/v1/admin/rtbf-requests — list RTBF requests with pagination */
export function useRtbfRequests(params: RtbfListParams = { page: 1, pageSize: 20 }) {
  return useQuery({
    queryKey: rtbfQueryKeys.list(params),
    queryFn: () =>
      apiClient
        .get<{ items: RtbfRequestDto[]; totalCount: number }>('/admin/rtbf-requests', {
          params,
        })
        .then((r) => r.data),
    placeholderData: (prev) => prev,
  });
}

// ─── Mutations ──────────────────────────────────────────────────────────────

/** POST /api/v1/admin/rtbf-requests — create a new RTBF request */
export function useCreateRtbf() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (dto: CreateRtbfRequest) =>
      apiClient.post<RtbfRequestDto>('/admin/rtbf-requests', dto).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['rtbf-requests'] }),
  });
}

/** POST /api/v1/admin/rtbf-requests/{id}/process — trigger erasure processing */
export function useProcessRtbf() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id }: { id: string }) =>
      apiClient
        .post(`/admin/rtbf-requests/${id}/process`)
        .then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['rtbf-requests'] }),
  });
}

/** POST /api/v1/admin/rtbf-requests/{id}/reject — reject with a reason */
export function useRejectRtbf() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      reason,
    }: {
      id: string;
      reason: string;
    }) =>
      apiClient
        .post(`/admin/rtbf-requests/${id}/reject`, { reason })
        .then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['rtbf-requests'] }),
  });
}
