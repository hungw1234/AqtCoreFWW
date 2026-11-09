import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';

// Route: /integration/contracts — integration contract list (M12)
export const integrationContractsRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/integration/contracts',
  component: lazyRouteComponent(
    () => import('@/features/integration'),
    'ContractListPage',
  ),
  validateSearch: (search: Record<string, unknown>) => ({
    page: Number(search['page'] ?? 1),
    pageSize: Number(search['pageSize'] ?? 20),
  }),
});
