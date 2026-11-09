import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';

// Route: / — KPI dashboard, requires auth (lazy loaded)
export const dashboardRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/',
  component: lazyRouteComponent(() => import('@/features/dashboard'), 'DashboardPage'),
});
