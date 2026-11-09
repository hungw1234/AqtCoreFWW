import { createRoute } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';
import { NewBpRunWizard } from '@/features/business-process/new-bp-run-wizard';
import { withModuleFlag } from '@/features/auth/module-flag-gate';

// Route: /business-process-runs/new — BP picker + intake form wizard
export const businessProcessRunNewRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/business-process-runs/new',
  component: withModuleFlag('businessProcess', NewBpRunWizard),
});
