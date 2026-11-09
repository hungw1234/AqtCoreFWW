import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/pending-workflow-links — admin manual-override for orphan
// FormSubmission→Workflow link retry rows (P6-DEFER-1 phase 06).
export const adminPendingWorkflowLinksRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/pending-workflow-links',
  component: lazyRouteComponent(
    () => import('@/features/forms/pending-workflow-links'),
    'PendingWorkflowLinksPage',
  ),
  validateSearch: (search: Record<string, unknown>) => ({
    page: Number(search['page'] ?? 1),
    pageSize: Number(search['pageSize'] ?? 20),
  }),
});
