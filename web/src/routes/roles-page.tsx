import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';

// Route: /roles — protected, requires login + roles.read permission
export const rolesRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/roles',
  component: lazyRouteComponent(() => import('@/features/roles'), 'RolesPage'),
});
