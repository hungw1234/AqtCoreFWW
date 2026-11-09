import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { PaginatedResult } from '@/shared/types/api';

// ─── Types ───────────────────────────────────────────────────────────────────

export type ExternalIdentityProvider = 'OAuth' | 'SSO' | 'LDAP' | 'SAML' | 'VNeID';

export interface ExternalIdentityDto {
  id: string;
  userId: string;
  provider: ExternalIdentityProvider;
  externalId: string;
  displayName?: string;
  email?: string;
  linkedAt: string;
  lastSyncAt?: string;
  isActive: boolean;
}

export interface CreateExternalIdentityDto {
  userId: string;
  provider: ExternalIdentityProvider;
  externalId: string;
  displayName?: string;
  email?: string;
}

export interface UpdateExternalIdentityDto {
  displayName?: string;
  email?: string;
  metadata?: Record<string, unknown>;
}

// ─── Query keys ──────────────────────────────────────────────────────────────

export const externalIdentitiesQueryKeys = {
  all: ['external-identities'] as const,
  byUser: (userId: string) => ['external-identities', 'user', userId] as const,
};

// ─── Queries ─────────────────────────────────────────────────────────────────

/**
 * GET /api/v1/admin/external-identities?userId={userId}
 * userId is optional — if omitted, lists all identities for admin view.
 */
export function useExternalIdentities(userId?: string) {
  return useQuery({
    queryKey: externalIdentitiesQueryKeys.byUser(userId ?? ''),
    queryFn: () =>
      apiClient
        .get<PaginatedResult<ExternalIdentityDto>>('/admin/external-identities', { params: userId ? { userId } : undefined })
        .then((r) => r.data),
    placeholderData: (prev) => prev,
  });
}

// ─── Mutations ───────────────────────────────────────────────────────────────

/** POST /api/v1/admin/external-identities — link a new external identity to a user */
export function useCreateExternalIdentity() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (dto: CreateExternalIdentityDto) =>
      apiClient
        .post<ExternalIdentityDto>('/admin/external-identities', dto)
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: externalIdentitiesQueryKeys.all });
    },
  });
}

/** PUT /api/v1/admin/external-identities/{id} — update display name, email, or metadata */
export function useUpdateExternalIdentity() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...dto }: UpdateExternalIdentityDto & { id: string }) =>
      apiClient
        .put<ExternalIdentityDto>(`/admin/external-identities/${id}`, dto)
        .then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: externalIdentitiesQueryKeys.all });
    },
  });
}

/** DELETE /api/v1/admin/external-identities/{id} — unlink external identity */
export function useDeleteExternalIdentity() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/admin/external-identities/${id}`).then((r) => r.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: externalIdentitiesQueryKeys.all });
    },
  });
}
