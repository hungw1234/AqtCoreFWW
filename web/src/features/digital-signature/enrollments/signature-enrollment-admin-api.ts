/**
 * signature-enrollment-admin-api.ts — hand-written hooks for the admin enrollment-approval page
 * (Phase 6 Slice C). Uses apiClient (interceptor unwraps {success,data}) → read r.data directly.
 *
 * Routes (BE-verified 2026-06-30), all JWT-tenant-scoped (H4 — override header ignored):
 *   GET  /admin/signer-cert-enrollments/pending?page&pageSize
 *   POST /admin/signer-cert-enrollments/{publicId}/approve
 *   POST /admin/signer-cert-enrollments/{publicId}/reject  { reason }
 *
 * approve/reject invalidate the pending queue so the actioned row drops on success.
 */

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { PendingEnrollmentsPage, RejectEnrollmentRequest } from './signature-enrollment-admin-types';

const BASE = '/admin/signer-cert-enrollments';

// ─── Query keys ──────────────────────────────────────────────────────────────

export const enrollmentAdminQueryKeys = {
  all: ['signer-cert-enrollments'] as const,
  pending: (page: number, pageSize: number) =>
    ['signer-cert-enrollments', 'pending', page, pageSize] as const,
};

// ─── Pending queue ───────────────────────────────────────────────────────────

/** GET /admin/signer-cert-enrollments/pending — the caller-tenant's Pending review queue (paginated). */
export function usePendingEnrollments(page: number, pageSize: number) {
  return useQuery({
    queryKey: enrollmentAdminQueryKeys.pending(page, pageSize),
    queryFn: async (): Promise<PendingEnrollmentsPage> => {
      const r = await apiClient.get<PendingEnrollmentsPage>(`${BASE}/pending`, {
        params: { page, pageSize },
      });
      return r.data;
    },
    placeholderData: (prev) => prev, // keep the table populated across page changes
  });
}

// ─── Approve / Reject ────────────────────────────────────────────────────────

/** POST /admin/signer-cert-enrollments/{publicId}/approve — 204, no body. */
export function useApproveEnrollment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (publicId: string) =>
      apiClient.post(`${BASE}/${publicId}/approve`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: enrollmentAdminQueryKeys.all }),
  });
}

/** POST /admin/signer-cert-enrollments/{publicId}/reject — 204, body { reason }. */
export function useRejectEnrollment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ publicId, reason }: { publicId: string } & RejectEnrollmentRequest) =>
      apiClient.post(`${BASE}/${publicId}/reject`, { reason }).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: enrollmentAdminQueryKeys.all }),
  });
}
