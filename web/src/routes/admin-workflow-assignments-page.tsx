import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/workflow-assignments — workflow assignment rules management
export const adminWorkflowAssignmentsRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/workflow-assignments',
  component: lazyRouteComponent(
    () => import('@/features/workflow'),
    'WorkflowAssignmentRulesPage',
  ),
});
