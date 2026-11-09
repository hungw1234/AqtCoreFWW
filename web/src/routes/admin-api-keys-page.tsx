import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/api-keys — API key management (create, list, revoke)
export const adminApiKeysRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/api-keys',
  component: lazyRouteComponent(() => import('@/features/api-keys'), 'ApiKeyListPage'),
});
