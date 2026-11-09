import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/security-health/backup-history — D7 backup/restore-drill history (QĐ 742 §6.1.b)
// Requires: SECURITY.HEALTH.VIEW permission
export const adminSecurityHealthBackupHistoryRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/security-health/backup-history',
  component: lazyRouteComponent(
    () => import('@/features/security-health/backup-history-page'),
    'BackupHistoryPage',
  ),
});
