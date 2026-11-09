import { useCallback, useEffect, useRef } from 'react';
import { useSearch, useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';

// Stable ref to avoid re-creating empty array on each render when no resetDeps provided
const emptyDeps: unknown[] = [];

interface PaginationState {
  page: number;
  pageSize: number;
}

interface UseServerPaginationReturn extends PaginationState {
  setPage: (page: number) => void;
  setPageSize: (size: number) => void;
  /** Returns query params object ready for API calls */
  toQueryParams: () => { pageNumber: number; pageSize: number };
  /** Ant Design Table pagination config */
  antPagination: {
    current: number;
    pageSize: number;
    showSizeChanger: boolean;
    showTotal: (total: number) => string;
    onChange: (page: number, size: number) => void;
  };
}

/**
 * Server-side pagination hook that syncs state with TanStack Router URL search params.
 * Keeps page/pageSize in the URL so the user can bookmark or share paginated views.
 *
 * @param defaultPageSize - default page size (default: 20)
 * @param resetDeps - when any value in this array changes, page resets to 1 (for search/filter changes)
 *
 * Usage:
 *   const { page, pageSize, toQueryParams, antPagination } = useServerPagination(20, [debouncedSearch, filters]);
 *   const { data } = useQuery({ queryKey: ['items', toQueryParams()], ... });
 *   <Table pagination={antPagination} ... />
 */
export function useServerPagination(defaultPageSize = 20, resetDeps?: unknown[]): UseServerPaginationReturn {
  const { t } = useTranslation();
  // TanStack Router exposes search params from the current matched route.
  // We cast to a permissive type because each route defines its own search schema.
  const search = useSearch({ strict: false }) as Record<string, unknown>;
  const navigate = useNavigate();

  const page = Number(search['page'] ?? 1);
  const pageSize = Number(search['pageSize'] ?? defaultPageSize);

  const updateSearch = useCallback(
    (updates: Partial<PaginationState>) => {
      // Cast to `any` to stay decoupled from individual route search schemas.
      // Each route that uses pagination must include `page` and `pageSize` in
      // its search validator, otherwise the router will strip the params.
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (navigate as any)({
        search: (prev: Record<string, unknown>) => ({
          ...prev,
          page: updates.page ?? page,
          pageSize: updates.pageSize ?? pageSize,
        }),
        replace: true,
      });
    },
    [navigate, page, pageSize],
  );

  const setPage = useCallback(
    (nextPage: number) => updateSearch({ page: nextPage }),
    [updateSearch],
  );

  const setPageSize = useCallback(
    (nextSize: number) => updateSearch({ page: 1, pageSize: nextSize }), // reset to page 1
    [updateSearch],
  );

  // Auto-reset to page 1 when search/filter deps change (skip initial mount)
  const isInitialRender = useRef(true);
  useEffect(() => {
    if (isInitialRender.current) {
      isInitialRender.current = false;
      return;
    }
    if (page !== 1) setPage(1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, resetDeps ?? emptyDeps);

  const toQueryParams = useCallback(
    () => ({ pageNumber: page, pageSize }),
    [page, pageSize],
  );

  const antPagination = {
    current: page,
    pageSize,
    showSizeChanger: true,
    showTotal: (total: number) => t('common.pagination.showTotal', { total, defaultValue: `Tổng ${total} bản ghi` }),
    onChange: (nextPage: number, nextSize: number) => {
      updateSearch({ page: nextPage, pageSize: nextSize });
    },
  };

  return { page, pageSize, setPage, setPageSize, toQueryParams, antPagination };
}
