import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/delegations — delegation management (create, list, revoke)
export const adminDelegationsRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/delegations',
  component: lazyRouteComponent(
    () => import('@/features/delegations/delegation-list-page'),
    'DelegationListPage',
  ),
});
