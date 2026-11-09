import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/menus — menu structure management (admin only)
export const adminMenusRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/menus',
  component: lazyRouteComponent(() => import('@/features/menus/menu-admin-page'), 'MenuAdminPage'),
});
