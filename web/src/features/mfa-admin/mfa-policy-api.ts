import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type {
  MfaPolicyDto,
  CreateMfaPolicyRequest,
  UpdateMfaPolicyRequest,
  ApiResponse,
} from './mfa-policy-types';

// ─── Query keys ──────────────────────────────────────────────────────────────

export const mfaPolicyQueryKeys = {
  all: ['mfa-policies'] as const,
  list: () => ['mfa-policies', 'list'] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/security/mfa-policies — list all per-role policies */
export function useMfaPolicies() {
  return useQuery({
    queryKey: mfaPolicyQueryKeys.list(),
    queryFn: () =>
      apiClient
        .get<ApiResponse<MfaPolicyDto[]>>('/admin/security/mfa-policies')
        .then((r) => r.data.data),
  });
}

// ─── Mutations ───────────────────────────────────────────────────────────────

/** POST /api/v1/admin/security/mfa-policies — create new per-role policy */
export function useCreateMfaPolicy() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateMfaPolicyRequest) =>
      apiClient
        .post<ApiResponse<string>>('/admin/security/mfa-policies', body)
        .then((r) => r.data.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: mfaPolicyQueryKeys.all });
    },
  });
}

/** PUT /api/v1/admin/security/mfa-policies/{id} — update an existing policy */
export function useUpdateMfaPolicy() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: UpdateMfaPolicyRequest & { id: string }) =>
      apiClient
        .put(`/admin/security/mfa-policies/${id}`, body)
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: mfaPolicyQueryKeys.all });
    },
  });
}
