import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';
import { withModuleFlag } from '@/features/auth/module-flag-gate';

// Route: /reports/definitions — report template list + create
export const reportDefinitionsRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/reports/definitions',
  component: withModuleFlag('reporting', lazyRouteComponent(() => import('@/features/reports'), 'ReportDefinitionsPage')),
});
