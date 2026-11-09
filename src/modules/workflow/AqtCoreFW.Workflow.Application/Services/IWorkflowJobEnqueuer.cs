namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// Application-layer abstraction over Hangfire IBackgroundJobClient.
/// Allows ExecuteVersionMigrationCommandHandler to enqueue jobs without
/// taking a direct Hangfire dependency in the Application project.
/// Implemented in Infrastructure by a thin Hangfire wrapper.
/// </summary>
public interface IWorkflowJobEnqueuer
{
    /// <summary>Enqueues the migration executor job. Returns the Hangfire job ID string.</summary>
    /// Phase-4b B4c: migrationId is long (T1 WorkflowVersionMigration.Id bigint PK).
    string EnqueueMigrationExecution(long migrationId);
}
