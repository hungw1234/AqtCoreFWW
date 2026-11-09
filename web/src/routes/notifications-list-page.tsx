import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';

// Route: /notifications — full notification list with pagination and mark-as-read
export const notificationsRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/notifications',
  component: lazyRouteComponent(() => import('@/features/notifications'), 'NotificationListPage'),
});
