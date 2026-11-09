import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/security-health/password-policy — D7 password complexity snapshot (QĐ 742 §1.5.a/1.5.b)
// Requires: SECURITY.HEALTH.VIEW permission
export const adminSecurityHealthPasswordPolicyRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/security-health/password-policy',
  component: lazyRouteComponent(
    () => import('@/features/security-health/password-policy-page'),
    'PasswordPolicyPage',
  ),
});
