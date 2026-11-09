import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/organization — org unit tree with staff assignment
export const adminOrganizationRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/organization',
  component: lazyRouteComponent(() => import('@/features/organization'), 'OrgTreePage'),
});
