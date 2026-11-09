import { useMemo } from 'react';
import { useAuth } from './use-auth';
import { useMyEffectivePermissions } from './me-permissions-api';

/**
 * Effective roles + permissions for the current user.
 * Source: GET /api/v1/me/permissions (Redis-cached TTL 10min server-side, RQ-cached client-side).
 * Roles also fall back to JWT `role` claim (Identity uppercase Code vs JWT Name) for early-render before API resolves.
 */
export function usePermissions() {
  const { user, isAuthenticated } = useAuth();
  const { data, isLoading, isFetching } = useMyEffectivePermissions(isAuthenticated);

  // True while authenticated user is awaiting first /me/permissions response.
  // Guards (RouteGuard / AdminGuard) MUST show spinner while loading to avoid
  // 403-flash on initial mount + after tenant-switch invalidation.
  const isPermissionsLoading = isAuthenticated && isLoading && !data;
  const isPermissionsRefetching = isAuthenticated && isFetching && !!data;

  const permissions = useMemo<string[]>(
    () => data?.permissionCodes ?? [],
    [data],
  );

  const roles = useMemo<string[]>(() => {
    if (data?.roles?.length) return data.roles;
    // Fallback to JWT role claim while permissions API is loading.
    const raw = (user?.profile as Record<string, unknown> | undefined)?.role;
    if (!raw) return [];
    return Array.isArray(raw) ? (raw as string[]) : [raw as string];
  }, [data, user]);

  const hasPermission = (perm: string) => permissions.includes(perm);
  const hasRole = (role: string) => roles.includes(role);

  return {
    permissions,
    roles,
    hasPermission,
    hasRole,
    isPermissionsLoading,
    isPermissionsRefetching,
  };
}
