import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/security-health/login-policy — D7 login/lockout policy snapshot (QĐ 742 §1.4.b/1.4.d)
// Requires: SECURITY.HEALTH.VIEW permission
export const adminSecurityHealthLoginPolicyRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/security-health/login-policy',
  component: lazyRouteComponent(
    () => import('@/features/security-health/login-policy-page'),
    'LoginPolicyPage',
  ),
});
