import { createRoute } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';
import { BusinessProcessDetailPage } from '@/features/business-process/business-process-detail-page';
import { withModuleFlag } from '@/features/auth/module-flag-gate';

// Route: /admin/business-processes/$id — BP detail (read-only + inline edit + activate/deactivate)
export const adminBusinessProcessDetailRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/business-processes/$id',
  component: withModuleFlag('businessProcess', function BusinessProcessDetailRouteComponent() {
    const { id } = adminBusinessProcessDetailRoute.useParams();
    return <BusinessProcessDetailPage id={id} />;
  }),
});
