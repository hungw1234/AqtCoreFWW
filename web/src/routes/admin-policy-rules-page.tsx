import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/policy-rules — access policy rules management
export const adminPolicyRulesRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/policy-rules',
  component: lazyRouteComponent(
    () => import('@/features/policy-rules/policy-rules-page'),
    'PolicyRulesPage',
  ),
});
