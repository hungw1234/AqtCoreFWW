// RT2-15 Phase 02 — hook for GET /api/v1/user-groups?activeOnly=true.
//
// Hand-written (orval regen requires running backend; not available at build time).
// Mirrors apiClient.get pattern from roles-api.ts:68-75.
//
// Auth: endpoint requires WORKFLOW.DEF.MANAGE — callers are workflow definition editors only.
// The axios response interceptor at api-client.ts:138-142 strips the success/data/meta/errors
// envelope before returning, so query.data IS the inner array at runtime.
//
// Default activeOnly=true — keeps hook call-sites clean; pass false only for admin scenarios.
// staleTime: 5 min — group list changes rarely during a single edit session.

import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

export interface UserGroupSummary {
  publicId: string;
  code: string;
  name: string;
  isActive: boolean;
}

export const userGroupsQueryKeys = {
  list: (activeOnly: boolean) => ['user-groups', 'list', { activeOnly }] as const,
};

export interface UseUserGroupsResult {
  data: UserGroupSummary[] | undefined;
  isLoading: boolean;
  error: unknown;
}

export function useUserGroups(opts?: { activeOnly?: boolean; background?: boolean }): UseUserGroupsResult {
  const activeOnly = opts?.activeOnly ?? true;

  const query = useQuery({
    queryKey: userGroupsQueryKeys.list(activeOnly),
    queryFn: () =>
      apiClient
        // background: cosmetic label lookup — swallow a 403 rather than firing an
        // interstitial; the caller falls back to the raw group code.
        .get<UserGroupSummary[]>('/user-groups', {
          params: { activeOnly },
          ...(opts?.background ? { suppressAuthInterstitial: true } : {}),
        })
        .then((r) => r.data),
    staleTime: 5 * 60 * 1000,
    // background: silence the global load-failed toast (react-query onError is a layer
    // above the api-client interceptor's suppressAuthInterstitial).
    ...(opts?.background ? { meta: { suppressErrorToast: true } } : {}),
  });

  return {
    data: query.data,
    isLoading: query.isLoading,
    error: query.error,
  };
}
