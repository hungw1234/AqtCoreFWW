// Menu tree API hook — fetches permission-filtered menu from BE

import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { NormalizedApiError } from '@/core/api/api-error';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface MenuNodeDto {
  id: string;
  code: string;
  title: string;
  description: string | null;
  icon: string | null;
  route: string | null;
  sortOrder: number;
  children: MenuNodeDto[];
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const menuQueryKeys = {
  tree: ['menus', 'tree'] as const,
};

// ─── Query ────────────────────────────────────────────────────────────────────

/**
 * GET /api/v1/menus/my-menus — returns permission-filtered menu tree.
 * RT-02: endpoint returns raw Ok(tree), NOT wrapped in ApiResponse envelope.
 * Axios interceptor only unwraps {success,data} shape — raw array passes through.
 * So r.data IS the MenuNodeDto[] directly.
 */
export function useMenuTree() {
  return useQuery({
    queryKey: menuQueryKeys.tree,
    queryFn: () =>
      apiClient.get<MenuNodeDto[]>('/menus/my-menus').then((r) => r.data),
    staleTime: 10 * 60 * 1000, // 10 min — matches Redis cache TTL
    // Skip retry on network/CORS (status=0) + 4xx; only retry transient 5xx.
    retry: (failureCount, error) => {
      const status = (error as unknown as NormalizedApiError | undefined)?.status;
      if (!status || status === 0) return false;
      if (status >= 400 && status < 500) return false;
      return failureCount < 2;
    },
  });
}
