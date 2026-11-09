import { createRoute } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';
import { WorkflowDefinitionDetailPage } from '@/features/workflow';

// Route: /admin/workflow/$definitionId — workflow definition detail with tabs
export const adminWorkflowDetailRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/workflow/$definitionId',
  component: function WorkflowDetailRouteComponent() {
    const { definitionId } = adminWorkflowDetailRoute.useParams();
    return <WorkflowDefinitionDetailPage definitionId={definitionId} />;
  },
});
