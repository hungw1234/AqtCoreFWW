import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';

// Route: /integration/partners — integration partner list (M12)
export const integrationPartnersRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/integration/partners',
  component: lazyRouteComponent(
    () => import('@/features/integration'),
    'PartnerListPage',
  ),
  validateSearch: (search: Record<string, unknown>) => ({
    page: Number(search['page'] ?? 1),
    pageSize: Number(search['pageSize'] ?? 20),
  }),
});
