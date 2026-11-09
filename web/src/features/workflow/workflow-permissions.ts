// Workflow feature permission constants — single source of truth for permission strings.
// Mirrors BE PermCodes.Workflow.* to prevent drift between FE guard strings and BE policy names.
export const WorkflowPermissions = {
  /** Grants access to the workflow analytics dashboard (/admin/workflow-analytics) */
  AnalyticsView: 'WORKFLOW.ANALYTICS.VIEW',

  /** View migration list and status */
  MigrationRead: 'WORKFLOW.MIGRATION.READ',
  /** Create migration draft and validate mappings */
  MigrationCreate: 'WORKFLOW.MIGRATION.CREATE',
  /** Approve a pending migration (creator !== currentUser enforced in UI) */
  MigrationApprove: 'WORKFLOW.MIGRATION.APPROVE',
  /** Execute an approved migration */
  MigrationExecute: 'WORKFLOW.MIGRATION.EXECUTE',
  /** Cancel migration in PendingApproval, Approved, or InProgress status */
  MigrationCancel: 'WORKFLOW.MIGRATION.CANCEL',
} as const;
