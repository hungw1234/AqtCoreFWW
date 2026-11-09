import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/backup — system backup/restore admin (NĐ53)
export const adminBackupRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/backup',
  component: lazyRouteComponent(
    () => import('@/features/backup/backup-admin-page'),
    'BackupAdminPage',
  ),
});
