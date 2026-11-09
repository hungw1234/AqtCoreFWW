import { createRoute } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';
import { BusinessProcessWizard } from '@/features/business-process/business-process-wizard';
import { withModuleFlag } from '@/features/auth/module-flag-gate';

// Route: /admin/business-processes/new — 2-step create wizard
export const adminBusinessProcessNewRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/business-processes/new',
  component: withModuleFlag('businessProcess', BusinessProcessWizard),
});
