import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types ────────────────────────────────────────────────────────────────────

// AuditCategory enum values — matches AuditCategory.cs (byte values 1-5)
export type AuditCategory = 'AppAccess' | 'AdminLogin' | 'Error' | 'AccountManagement' | 'ConfigChange';

export interface AuditRetentionPolicyDto {
  id: number; // long T1 entity
  tenantId?: string;
  category?: AuditCategory;
  retentionDays: number;
  archiveBeforePurge: boolean;
  archivePath?: string;
  rowVersion: string; // base64-encoded; must be echoed on PUT/DELETE
  createdAt: string; // ISO DateTimeOffset
  updatedAt?: string;
}

export interface CreateAuditRetentionPolicyRequest {
  tenantId?: string;
  category?: AuditCategory;
  retentionDays: number;
  archiveBeforePurge: boolean;
  archivePath?: string;
}

export interface UpdateAuditRetentionPolicyRequest {
  retentionDays: number;
  archiveBeforePurge: boolean;
  archivePath?: string;
  rowVersion: string; // optimistic concurrency token
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const auditPolicyQueryKeys = {
  all: ['audit-retention-policies'] as const,
  list: ['audit-retention-policies', 'list'] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/** GET /api/v1/audit/retention-policies — list policies visible to caller */
export function useAuditRetentionPolicies() {
  return useQuery({
    queryKey: auditPolicyQueryKeys.list,
    queryFn: () =>
      apiClient
        .get<AuditRetentionPolicyDto[]>('/audit/retention-policies')
        .then((r) => r.data),
  });
}

// ─── Mutations ────────────────────────────────────────────────────────────────

/** POST /api/v1/audit/retention-policies — create a new retention rule */
export function useCreateAuditRetentionPolicy() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (dto: CreateAuditRetentionPolicyRequest) =>
      apiClient
        .post<number>('/audit/retention-policies', dto)
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: auditPolicyQueryKeys.all });
    },
  });
}

/** PUT /api/v1/audit/retention-policies/{id} — update retention window (RowVersion required) */
export function useUpdateAuditRetentionPolicy() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...dto }: UpdateAuditRetentionPolicyRequest & { id: number }) =>
      apiClient
        .put(`/audit/retention-policies/${id}`, dto)
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: auditPolicyQueryKeys.all });
    },
  });
}

/**
 * DELETE /api/v1/audit/retention-policies/{id}?rowVersion=...
 * RowVersion sent as query param (BE uses [FromQuery]).
 */
export function useDeleteAuditRetentionPolicy() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, rowVersion }: { id: number; rowVersion: string }) =>
      apiClient
        .delete(`/audit/retention-policies/${id}`, { params: { rowVersion } })
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: auditPolicyQueryKeys.all });
    },
  });
}
