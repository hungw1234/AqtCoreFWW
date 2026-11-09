import { createRoute } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';
import { InstanceDetailPage } from '@/features/workflow';

// Route: /admin/workflow/instances/$instanceId — single instance detail
export const adminWorkflowInstanceDetailRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/workflow/instances/$instanceId',
  component: function WorkflowInstanceDetailRouteComponent() {
    const { instanceId } = adminWorkflowInstanceDetailRoute.useParams();
    return <InstanceDetailPage instanceId={instanceId} />;
  },
});
