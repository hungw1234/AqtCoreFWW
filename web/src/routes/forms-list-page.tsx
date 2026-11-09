import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';

// Route: /forms — form templates list
export const formsRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/forms',
  component: lazyRouteComponent(() => import('@/features/forms'), 'FormTemplatesPage'),
});
