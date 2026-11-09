using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Domain.Entities;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence;

/// <summary>
/// Generic repository for WorkflowAssignmentRule.
/// WorkflowAssignmentRule is T1 (long Id only, no PublicId).
/// Resolution queries use Dapper via IReadDbConnection in WorkflowAssignmentResolver.
/// </summary>
public sealed class WorkflowAssignmentRuleRepository(WorkflowDbContext context)
    : GenericRepository<WorkflowAssignmentRule, long>(context);
