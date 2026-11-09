// Menu API hooks — wraps /api/v1/menus/* endpoints (Identity module)

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface MenuDto {
  id: string;
  parentId?: string | null;
  code: string;
  title: string;
  icon?: string | null;
  route?: string | null;
  sortOrder: number;
  isActive: boolean;
  tenantId?: string | null;
  permissionCodes: string[];
}

export interface CreateMenuRequest {
  parentId?: string | null;
  code: string;
  title: string;
  icon?: string | null;
  route?: string | null;
  sortOrder: number;
  permissionCodes?: string[];
}

export interface UpdateMenuRequest {
  parentId?: string | null;
  title: string;
  icon?: string | null;
  route?: string | null;
  sortOrder: number;
  isActive: boolean;
  permissionCodes?: string[];
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const menuQueryKeys = {
  all: ['menus'] as const,
  admin: ['menus', 'admin'] as const,
  myMenus: ['menus', 'my'] as const,
};

// ─── Queries ──────────────────────────────────────────────────────────────────

/** GET /api/v1/menus/admin/menus — full list for admin management */
export function useMenus() {
  return useQuery({
    queryKey: menuQueryKeys.admin,
    queryFn: () =>
      apiClient
        .get<MenuDto[]>('/menus/admin/menus')
        .then((r) => r.data),
  });
}

/** GET /api/v1/menus/my-menus — authorized menu tree for current user */
export function useMyMenus() {
  return useQuery({
    queryKey: menuQueryKeys.myMenus,
    queryFn: () =>
      apiClient.get<MenuDto[]>('/menus/my-menus').then((r) => r.data),
  });
}

// ─── Mutations ────────────────────────────────────────────────────────────────

/** POST /api/v1/menus/admin/menus */
export function useCreateMenu() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateMenuRequest) =>
      apiClient.post<{ id: string }>('/menus/admin/menus', body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: menuQueryKeys.all }),
  });
}

/** PUT /api/v1/menus/admin/menus/{id} */
export function useUpdateMenu() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateMenuRequest }) =>
      apiClient.put(`/menus/admin/menus/${id}`, body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: menuQueryKeys.all }),
  });
}
