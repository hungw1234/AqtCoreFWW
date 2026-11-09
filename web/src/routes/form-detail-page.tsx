import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';

// Route: /forms/$id — form builder (Draft) or read-only view (Active/Inactive)
export const formDetailRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/forms/$id',
  component: lazyRouteComponent(() => import('@/features/forms'), 'FormBuilderPage'),
});
