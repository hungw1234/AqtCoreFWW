import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/data-scopes — role-based data scope assignment
export const adminDataScopesRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/data-scopes',
  component: lazyRouteComponent(
    () => import('@/features/data-scopes/data-scopes-page'),
    'DataScopesPage',
  ),
});
