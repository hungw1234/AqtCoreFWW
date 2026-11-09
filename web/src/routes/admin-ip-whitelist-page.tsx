import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/ip-whitelist — IP filter rule CRUD (QĐ 742 §2.2.a)
// Requires: SECURITY.IP_RULE.VIEW permission
export const adminIpWhitelistRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/ip-whitelist',
  component: lazyRouteComponent(
    () => import('@/features/network-policies/ip-whitelist-page'),
    'IpWhitelistPage',
  ),
});
