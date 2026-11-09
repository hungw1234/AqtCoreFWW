import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/sessions — active session management (view, revoke single, revoke all)
// validateSearch syncs page/pageSize to URL so useServerPagination can read them
export const adminSessionsRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/sessions',
  component: lazyRouteComponent(
    () => import('@/features/sessions/session-admin-page'),
    'SessionAdminPage',
  ),
  validateSearch: (search: Record<string, unknown>) => ({
    page: Number(search['page'] ?? 1),
    pageSize: Number(search['pageSize'] ?? 20),
  }),
});
