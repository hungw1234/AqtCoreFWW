import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/health — system health check, requires auth (lazy loaded)
export const adminHealthRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/health',
  component: lazyRouteComponent(() => import('@/features/admin/health-check-page'), 'HealthCheckPage'),
});
