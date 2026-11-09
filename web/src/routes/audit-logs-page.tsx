import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';

// Route: /audit/logs — protected, requires login
export const auditLogsRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/audit/logs',
  component: lazyRouteComponent(() => import('@/features/audit'), 'AuditLogPage'),
  validateSearch: (search: Record<string, unknown>) => ({
    page: Number(search['page'] ?? 1),
    pageSize: Number(search['pageSize'] ?? 20),
  }),
});
