using AqtCoreFW.Workflow.Application.Services;
using Hangfire;

namespace AqtCoreFW.Workflow.Infrastructure.Services;

/// <summary>
/// Hangfire-backed implementation of IWorkflowJobEnqueuer.
/// Wraps IBackgroundJobClient so Application layer stays free of Hangfire dependency.
/// </summary>
public sealed class WorkflowJobEnqueuer(IBackgroundJobClient jobClient) : IWorkflowJobEnqueuer
{
    // migrationId is long (T1 WorkflowVersionMigration.Id bigint PK — Phase-4b B4c)
    public string EnqueueMigrationExecution(long migrationId) =>
        jobClient.Enqueue<IWorkflowMigrationExecutor>(x => x.ExecuteAsync(migrationId));
}
