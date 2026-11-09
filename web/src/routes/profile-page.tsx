import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';

// Route: /profile — user profile page, requires auth (lazy loaded)
export const profileRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/profile',
  component: lazyRouteComponent(() => import('@/features/profile/profile-page'), 'ProfilePage'),
});
