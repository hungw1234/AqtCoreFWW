import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/master-data/workflow-actions — Phase-00 C4b-2 admin CRUD for workflow action taxonomy
export const adminWorkflowActionsRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/master-data/workflow-actions',
  component: lazyRouteComponent(
    () => import('@/features/workflow/workflow-action-taxonomy-admin-page'),
    'WorkflowActionTaxonomyAdminPage',
  ),
});
