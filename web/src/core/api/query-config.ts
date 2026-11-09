import { QueryClient, QueryCache, type Query } from '@tanstack/react-query';
import i18next from 'i18next';
import { getNotificationInstance } from './notification-bridge';
import type { NormalizedApiError } from './api-error';

// Allowlist of query-key root strings that MUST survive a tenant switch.
// These caches are NOT tenant-scoped: auth (OIDC user), i18n, session-config are global.
// NOTE (v2.133p6): the `me` query root (used by /api/v1/me/permissions) and `menus` root
// (`/menus/my-menus`) are INTENTIONALLY tenant-scoped — effective perms + menu shape differ
// per tenant override, so they must invalidate. Both deliberately omitted from this list.
// NOTE (v2.133p8 RBAC-5): legacy entries `'permissions'` and `'menu'` (singular) were
// removed — no current query key uses them; modern roots are `me` + `menus` (plural).
export const PRESERVED_KEY_PREFIXES = ['auth', 'i18n', 'session'] as const;

export function shouldInvalidateOnTenantSwitch(query: Pick<Query, 'queryKey'>): boolean {
  const root = query.queryKey[0];
  if (typeof root !== 'string') return true;
  return !(PRESERVED_KEY_PREFIXES as readonly string[]).includes(root);
}

// Retry policy: skip noisy retries that won't change outcome.
// - Network errors (status 0/undefined) won't recover within 1s — react-query retry just doubles request count.
// - 4xx is client-side and won't change on retry. 401 has its own silent-renew + replay path in api-client.ts.
// - 5xx may be transient — retry once with backoff.
export function shouldRetryQuery(failureCount: number, error: unknown): boolean {
  const status = (error as NormalizedApiError | undefined)?.status;
  if (!status || status === 0) return false;
  if (status >= 400 && status < 500) return false;
  return failureCount < 1;
}

export function queryRetryDelay(attempt: number): number {
  return Math.min(1000 * 2 ** attempt, 8000);
}

// Singleton QueryClient with GOV-appropriate defaults.
export const queryClient = new QueryClient({
  queryCache: new QueryCache({
    onError: (error: unknown, query) => {
      const apiErr = error as NormalizedApiError;
      // 401 is owned by the auth flow — AuthProvider renders a dedicated countdown toast
      // and auto-redirects. A generic "load failed" toast here would duplicate + confuse.
      if (apiErr?.status === 401) return;
      // Cosmetic/background queries (e.g. NextHandlerCard label lookups) opt out of the
      // generic load-failed toast: a non-critical lookup that 403s must fail silently and
      // degrade to raw identifiers, not stack a toast per failed lookup.
      if ((query?.meta as { suppressErrorToast?: boolean } | undefined)?.suppressErrorToast === true) return;
      const n = getNotificationInstance();
      if (n) {
        n.error({
          message: apiErr?.message ?? i18next.t('error.loadFailed', { defaultValue: 'Đã xảy ra lỗi khi tải dữ liệu' }),
          description: apiErr?.detail,
          duration: 5,
        });
      }
    },
  }),
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      gcTime: 5 * 60 * 1000,
      retry: shouldRetryQuery,
      retryDelay: queryRetryDelay,
      refetchOnWindowFocus: false,
    },
    mutations: {
      retry: 0,
      onError: (error: unknown) => {
        if (import.meta.env.DEV) {
          const apiErr = error as NormalizedApiError;
          console.error(
            `[API Error] ${apiErr?.status ?? '?'} ${apiErr?.code ?? ''}: ${apiErr?.message ?? error}`,
          );
        }
      },
    },
  },
});
