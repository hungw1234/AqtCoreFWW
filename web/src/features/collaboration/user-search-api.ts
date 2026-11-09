// user-search-api.ts — public user search for chat participant picker
// Uses GET /api/v1/users/search — returns minimal fields (no email)

import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

export interface UserSearchResult {
  id: string;
  fullName: string;
  departmentCode?: string;
}

/** Search users by name — debounced, min 2 chars enforced by caller */
export function useUserSearch(query: string, enabled: boolean) {
  return useQuery({
    queryKey: ['users', 'search', query],
    queryFn: () =>
      // apiClient interceptor already unwraps { success, data } envelope → r.data IS the array
      apiClient
        .get<UserSearchResult[]>('/users/search', { params: { q: query, pageSize: 20 } })
        .then((r) => r.data ?? []),
    enabled: enabled && query.length >= 2,
    staleTime: 30_000,
  });
}
