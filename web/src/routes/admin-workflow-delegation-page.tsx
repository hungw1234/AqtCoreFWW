import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/workflow-delegation — delegation rules management page
// Access gated by WORKFLOW.DELEGATION.MANAGE permission (also enforced by BE controller).
export const adminWorkflowDelegationRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/workflow-delegation',
  component: lazyRouteComponent(
    () => import('@/features/workflow'),
    'DelegationRulesPage',
  ),
});
