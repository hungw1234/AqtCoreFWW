import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/notification-templates — admin notification template management
export const adminNotificationTemplatesRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/notification-templates',
  component: lazyRouteComponent(
    () => import('@/features/notifications/notification-templates-admin-page'),
    'NotificationTemplatesAdminPage',
  ),
});
