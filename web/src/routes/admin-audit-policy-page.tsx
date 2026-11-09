import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/audit-policy — audit log retention policy CRUD (QĐ 742 §3.2.a/b)
// Requires: AUDIT.RETENTION.VIEW permission
export const adminAuditPolicyRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/audit-policy',
  component: lazyRouteComponent(
    () => import('@/features/audit-policy/audit-policy-page'),
    'AuditPolicyPage',
  ),
});
