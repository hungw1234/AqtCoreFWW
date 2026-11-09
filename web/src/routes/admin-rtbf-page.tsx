import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/rtbf — Right to Be Forgotten request management (admin)
export const adminRtbfRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/rtbf',
  component: lazyRouteComponent(
    () => import('@/features/rtbf/rtbf-page'),
    'RtbfPage',
  ),
});
