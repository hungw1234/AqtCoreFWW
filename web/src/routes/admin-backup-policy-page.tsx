import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/backup-policy — backup & restore-drill management (QĐ 742 §6.1.a)
// Requires: SYSTEM.CONFIG.MANAGE permission
export const adminBackupPolicyRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/backup-policy',
  component: lazyRouteComponent(
    () => import('@/features/backup-policy/backup-policy-page'),
    'BackupPolicyPage',
  ),
});
