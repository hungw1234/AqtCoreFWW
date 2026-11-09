using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;

namespace AqtCoreFW.Workflow.Application.Repositories;

/// <summary>
/// Write-side repository for TaskAssignment audit records.
/// Only needs AddAsync — assignments are append-only history entries.
/// </summary>
public interface ITaskAssignmentRepository
{
    Task AddAsync(TaskAssignment assignment, CancellationToken cancellationToken = default);
}
