import { createRoute } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';
import { BpRunDetailPage } from '@/features/business-process/bp-run-detail-page';
import { withModuleFlag } from '@/features/auth/module-flag-gate';

// Route: /business-process-runs/$id — BP Run detail with header + actions + state forms tabs
export const businessProcessRunDetailRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/business-process-runs/$id',
  component: withModuleFlag('businessProcess', function BpRunDetailRouteComponent() {
    const { id } = businessProcessRunDetailRoute.useParams();
    return <BpRunDetailPage workflowInstanceId={id} />;
  }),
});
