import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/sod-rules — Segregation of Duties conflict rules
export const adminSodRulesRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/sod-rules',
  component: lazyRouteComponent(
    () => import('@/features/sod-rules/sod-rules-page'),
    'SodRulesPage',
  ),
});
