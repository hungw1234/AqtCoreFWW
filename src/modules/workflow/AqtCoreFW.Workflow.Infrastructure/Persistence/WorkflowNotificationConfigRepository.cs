using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Domain.Entities;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence;

/// <summary>
/// Generic repository for WorkflowNotificationConfig.
/// WorkflowNotificationConfig is T1 (long Id only, no PublicId).
/// </summary>
public sealed class WorkflowNotificationConfigRepository(WorkflowDbContext context)
    : GenericRepository<WorkflowNotificationConfig, long>(context);
