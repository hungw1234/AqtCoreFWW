import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/security-health/key-rotation — D7 encryption key lifecycle (QĐ 742 §4.4.a)
// Requires: SECURITY.HEALTH.VIEW permission
export const adminSecurityHealthKeyRotationRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/security-health/key-rotation',
  component: lazyRouteComponent(
    () => import('@/features/security-health/key-rotation-page'),
    'KeyRotationPage',
  ),
});
