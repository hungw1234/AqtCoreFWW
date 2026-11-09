import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/jobs — job monitor (recurring jobs, history, dead letters) (P1-11)
export const adminJobsRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/jobs',
  component: lazyRouteComponent(
    () => import('@/features/jobs'),
    'JobMonitorPage',
  ),
  validateSearch: (search: Record<string, unknown>) => ({
    page: Number(search['page'] ?? 1),
    pageSize: Number(search['pageSize'] ?? 20),
  }),
});
