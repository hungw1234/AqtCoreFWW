import { createRoute } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';
import { BusinessProcessesListPage } from '@/features/business-process/business-processes-list-page';
import { withModuleFlag } from '@/features/auth/module-flag-gate';

// Route: /admin/business-processes — BP admin list
export const adminBusinessProcessesRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/business-processes',
  component: withModuleFlag('businessProcess', BusinessProcessesListPage),
});
