import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/mfa-policies — per-role MFA policy management (QĐ742 1.6.a)
// Requires: SECURITY.MFA_POLICY.VIEW permission (enforced by BE [RequiresMfa] + Authorize policy)
export const adminMfaPoliciesRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/mfa-policies',
  component: lazyRouteComponent(
    () => import('@/features/mfa-admin/mfa-policy-page'),
    'MfaPolicyPage',
  ),
});
