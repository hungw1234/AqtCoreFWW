using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence;

/// <summary>
/// Append-only repository for TaskAssignment audit records.
/// AddAsync only tracks the entity in the EF change tracker — does NOT call SaveChangesAsync.
/// The calling command handler calls DbContext.SaveChangesAsync() once after all mutations
/// to ensure atomic task + assignment saves.
/// </summary>
public sealed class TaskAssignmentRepository(WorkflowDbContext context)
    : ITaskAssignmentRepository
{
    public async Task AddAsync(TaskAssignment assignment, CancellationToken cancellationToken = default)
    {
        // Track only — caller flushes via DbContext.SaveChangesAsync()
        await context.TaskAssignments.AddAsync(assignment, cancellationToken);
    }
}
