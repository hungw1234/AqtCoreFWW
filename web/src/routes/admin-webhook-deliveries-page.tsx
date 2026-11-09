import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/webhooks — webhook delivery logs (select subscription, view attempts)
export const adminWebhookDeliveriesRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/webhooks',
  component: lazyRouteComponent(
    () => import('@/features/webhooks/webhook-deliveries-page'),
    'WebhookDeliveriesPage',
  ),
});
