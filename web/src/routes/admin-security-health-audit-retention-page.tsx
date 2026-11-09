import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/security-health/audit-retention — D7 audit retention config snapshot (QĐ 742 §3.2.a/b)
// Requires: SECURITY.HEALTH.VIEW permission
export const adminSecurityHealthAuditRetentionRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/security-health/audit-retention',
  component: lazyRouteComponent(
    () => import('@/features/security-health/audit-retention-page'),
    'AuditRetentionPage',
  ),
});
