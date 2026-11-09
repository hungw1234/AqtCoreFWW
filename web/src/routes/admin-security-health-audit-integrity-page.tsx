import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/security-health/audit-integrity — D7 immutability trigger health (QĐ 742 §3.4.a)
// Requires: SECURITY.HEALTH.VIEW permission
export const adminSecurityHealthAuditIntegrityRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/security-health/audit-integrity',
  component: lazyRouteComponent(
    () => import('@/features/security-health/audit-integrity-page'),
    'AuditIntegrityPage',
  ),
});
