import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';
import { withModuleFlag } from '@/features/auth/module-flag-gate';

// Route: /reports/executions — execution history + polling + download
export const reportExecutionsRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/reports/executions',
  component: withModuleFlag('reporting', lazyRouteComponent(() => import('@/features/reports'), 'ReportExecutionsPage')),
});
