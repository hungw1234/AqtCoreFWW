import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/master-data — cascading Province → District → Ward viewer
export const adminMasterDataRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/master-data',
  component: lazyRouteComponent(() => import('@/features/master-data'), 'MasterDataPage'),
});
