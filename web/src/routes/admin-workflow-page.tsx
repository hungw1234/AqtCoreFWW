import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/workflow — workflow definitions management
export const adminWorkflowRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/workflow',
  component: lazyRouteComponent(() => import('@/features/workflow'), 'WorkflowAdminPage'),
});
