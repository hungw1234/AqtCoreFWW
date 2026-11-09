import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/system-params — system configuration, feature flags, announcements
export const adminSystemParamsRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/system-params',
  component: lazyRouteComponent(() => import('@/features/system-params'), 'SystemParamsPage'),
});
