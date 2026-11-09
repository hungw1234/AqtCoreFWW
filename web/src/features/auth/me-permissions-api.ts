// Effective permissions API hook — fetches current user's roles + permission codes
// from /api/v1/me/permissions. Replaces JWT-stuffed permission claim approach.

import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { NormalizedApiError } from '@/core/api/api-error';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface MyPermissionsDto {
  userId: string;
  roles: string[];
  permissionCodes: string[];
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const mePermissionsQueryKeys = {
  permissions: ['me', 'permissions'] as const,
};

// ─── Query ────────────────────────────────────────────────────────────────────

/**
 * GET /api/v1/me/permissions — returns effective roles + permission codes for current user.
 * Server-side computed via IEffectivePermissionService (Redis-cached TTL 10min):
 * direct roles ∪ group roles ∪ active delegations.
 *
 * Cache TTL on FE matches server cache to avoid redundant calls.
 */
export function useMyEffectivePermissions(enabled: boolean = true) {
  return useQuery({
    queryKey: mePermissionsQueryKeys.permissions,
    queryFn: () =>
      apiClient.get<MyPermissionsDto>('/me/permissions').then((r) => r.data),
    staleTime: 10 * 60 * 1000,
    // Skip retry on network/CORS (status=0) and 4xx — they won't recover within
    // a 1-2s backoff window. Only transient 5xx warrants a retry.
    retry: (failureCount, error) => {
      const status = (error as unknown as NormalizedApiError | undefined)?.status;
      if (!status || status === 0) return false;
      if (status >= 400 && status < 500) return false;
      return failureCount < 2;
    },
    enabled,
  });
}
