import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/master-data/workflow-states — Phase-00 C4b-1 admin CRUD for workflow state taxonomy
export const adminWorkflowStatesRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/master-data/workflow-states',
  component: lazyRouteComponent(
    () => import('@/features/workflow/workflow-state-taxonomy-admin-page'),
    'WorkflowStateTaxonomyAdminPage',
  ),
});
