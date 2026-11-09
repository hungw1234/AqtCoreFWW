// Route: /admin/workflow/$definitionId/migrations/$migrationId
// Shows WorkflowMigrationStatusPanel for a specific migration.
// Requires WORKFLOW.MIGRATION.READ permission.

import { createRoute } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';
import { usePermissions } from '@/features/auth';
import { ForbiddenPage } from '@/shared/components/forbidden-page';
import { WorkflowPermissions } from '@/features/workflow/workflow-permissions';
import { WorkflowMigrationPage } from '@/features/workflow/migration/workflow-migration-page';
import { useWorkflowDefinition } from '@/features/workflow/workflow-api';
import { Spin } from 'antd';

function MigrationDetailGuard() {
  const { definitionId, migrationId } = adminWorkflowMigrationDetailRoute.useParams();
  const { hasPermission } = usePermissions();
  const { data: definition, isLoading } = useWorkflowDefinition(definitionId);

  if (!hasPermission(WorkflowPermissions.MigrationRead)) return <ForbiddenPage />;
  if (isLoading || !definition) return <Spin style={{ display: 'block', margin: '80px auto' }} />;

  return (
    <WorkflowMigrationPage
      definitionId={definitionId}
      definitionKey={definition.definitionKey}
      migrationId={migrationId}
    />
  );
}

export const adminWorkflowMigrationDetailRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/workflow/$definitionId/migrations/$migrationId',
  component: MigrationDetailGuard,
});
