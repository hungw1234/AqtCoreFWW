import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/abac-rules — attribute-based access control rules management
export const adminAbacRulesRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/abac-rules',
  component: lazyRouteComponent(
    () => import('@/features/abac-rules/abac-rules-page'),
    'AbacRulesPage',
  ),
});
