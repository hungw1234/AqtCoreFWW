namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// Hangfire-invocable interface for the version migration batch executor.
/// Defined in Application layer so command handlers can enqueue without
/// taking an Infrastructure dependency.
/// Implementation (WorkflowMigrationExecutor) lives in Workflow.Infrastructure/Jobs.
/// Chunk C will provide the concrete implementation.
/// </summary>
public interface IWorkflowMigrationExecutor
{
    /// <summary>
    /// Execute the migration batch for the given migration ID.
    /// Called by Hangfire after ExecuteVersionMigrationCommand sets Status=InProgress.
    /// Must be idempotent — Hangfire may retry after mid-batch crash.
    /// </summary>
    /// Phase-4b B4c: migrationId is long (T1 WorkflowVersionMigration.Id bigint PK).
    Task ExecuteAsync(long migrationId);
}
