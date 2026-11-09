// Route: /admin/workflow-analytics — analytics dashboard for workflow KPIs
// Permission gate: WORKFLOW.ANALYTICS.VIEW — shows ForbiddenPage if missing
import { createRoute } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';
import { usePermissions } from '@/features/auth';
import { ForbiddenPage } from '@/shared/components/forbidden-page';
import { WorkflowAnalyticsDashboard } from '@/features/workflow';
import { WorkflowPermissions } from '@/features/workflow/workflow-permissions';

function AnalyticsGuard() {
  const { hasPermission } = usePermissions();
  if (!hasPermission(WorkflowPermissions.AnalyticsView)) {
    return <ForbiddenPage />;
  }
  return <WorkflowAnalyticsDashboard />;
}

export const adminWorkflowAnalyticsRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/workflow-analytics',
  component: AnalyticsGuard,
});
