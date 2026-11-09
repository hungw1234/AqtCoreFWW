import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { PaginatedResult } from '@/shared/types/api';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface ActiveSessionDto {
  tokenId: string;
  userId: string;
  userEmail?: string;       // BE field: UserEmail (resolved from AspNetUsers JOIN)
  issuedAt: string;         // BE field: IssuedAt
  expiresAt: string;        // BE field: ExpiresAt
  ipAddress: string | null;
  clientId: string | null;  // BE field: ClientId
}

export interface ActiveSessionParams {
  userId?: string;
  page?: number;
  pageSize?: number;
}

// ─── Query keys ─────────────────────────────────────────────────────────────

export const sessionQueryKeys = {
  active: (params: ActiveSessionParams) => ['sessions', 'active', params] as const,
};

// ─── Queries ────────────────────────────────────────────────────────────────

/** GET /api/v1/admin/sessions/active — list active sessions, server-paginated */
export function useActiveSessions(params: ActiveSessionParams) {
  return useQuery({
    queryKey: sessionQueryKeys.active(params),
    queryFn: () =>
      apiClient
        .get<PaginatedResult<ActiveSessionDto>>('/admin/sessions/active', { params })
        .then((r) => r.data),
    placeholderData: (prev) => prev,
  });
}

// ─── Mutations ──────────────────────────────────────────────────────────────

/** DELETE /api/v1/admin/sessions/{tokenId} — revoke a single session */
export function useRevokeSession() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (tokenId: string) =>
      apiClient.delete(`/admin/sessions/${tokenId}`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['sessions'] });
    },
  });
}

/** DELETE /api/v1/admin/sessions/user/{userId} — revoke all sessions for a user */
export function useRevokeUserSessions() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (userId: string) =>
      apiClient.delete(`/admin/sessions/user/${userId}`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['sessions'] });
    },
  });
}
