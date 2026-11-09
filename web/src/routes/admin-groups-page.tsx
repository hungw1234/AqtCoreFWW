import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/groups — user group management with members and roles
export const adminGroupsRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/groups',
  component: lazyRouteComponent(
    () => import('@/features/groups/groups-page'),
    'GroupsPage',
  ),
});
