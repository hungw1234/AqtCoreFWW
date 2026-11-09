import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/credential-policies — password policy management
export const adminCredentialPoliciesRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/credential-policies',
  component: lazyRouteComponent(
    () => import('@/features/credential-policies/credential-policies-page'),
    'CredentialPoliciesPage',
  ),
});
