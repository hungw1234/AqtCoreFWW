import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';

// Route: /consent — user-facing PDPL consent management page
export const consentRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/consent',
  component: lazyRouteComponent(
    () => import('@/features/consent/consent-page'),
    'ConsentPage',
  ),
});
