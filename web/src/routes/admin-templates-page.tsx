import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/templates — document template management (P1-07)
export const adminTemplatesRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/templates',
  component: lazyRouteComponent(
    () => import('@/features/templates'),
    'TemplateListPage',
  ),
  validateSearch: (search: Record<string, unknown>) => ({
    page: Number(search['page'] ?? 1),
    pageSize: Number(search['pageSize'] ?? 20),
  }),
});
