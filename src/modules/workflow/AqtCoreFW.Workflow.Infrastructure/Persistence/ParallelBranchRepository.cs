using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence;

/// <summary>
/// EF Core repository for WorkflowParallelBranch aggregate.
/// T1 entity: IRepository key is long (bigint PK). GenericRepository&lt;T, long&gt;.
/// Phase-4b B4c: changed from GenericRepository&lt;T, Guid&gt; to long.
///   GetBranchByStateIdAsync: params changed Guid→long (DefinitionId/BranchingStateId are long FKs).
///   GetInstanceBranchStatusAsync: param is Guid instancePublicId (T2 WorkflowInstance.PublicId AK).
///   GetBranchStatusByChildStatusIdAsync: param changed Guid→long (WorkflowBranchChildStatus.Id is long PK).
/// </summary>
public sealed class ParallelBranchRepository(WorkflowDbContext context)
    : GenericRepository<WorkflowParallelBranch, long>(context), IParallelBranchRepository
{
    private WorkflowDbContext Ctx => (WorkflowDbContext)Context;

    /// <inheritdoc/>
    public async Task<WorkflowParallelBranch?> GetBranchByStateIdAsync(
        long definitionInternalId,
        long stateInternalId,
        CancellationToken ct = default)
    {
        return await Ctx.ParallelBranches
            .Include(b => b.Children)
                .ThenInclude(c => c.Resolutions)
            .Include(b => b.Children)
                // Per-child pool rule — fork fan-out resolves these clauses → candidate-index.
                // Without this the runtime resolve sees a null Assignment → empty pool.
                .ThenInclude(c => c.Assignment!)
                    .ThenInclude(a => a.Clauses)
            // Two collection-include paths under Children (Resolutions + Assignment.Clauses) would
            // cartesian-multiply rows in one JOIN — split into separate round-trips (matches
            // GetWithStatesAndTransitionsAsync). This is a per-fork-fan-out runtime read.
            .AsSplitQuery()
            .FirstOrDefaultAsync(
                b => b.DefinitionId == definitionInternalId && b.BranchingStateId == stateInternalId,
                ct);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// instancePublicId = WorkflowInstance.PublicId (T2 Guid AK).
    /// Joins to WorkflowInstances to resolve the internal long Id used as InstanceId FK.
    /// </remarks>
    public async Task<WorkflowInstanceBranchStatus?> GetInstanceBranchStatusAsync(
        Guid instancePublicId,
        CancellationToken ct = default)
    {
        // Resolve PublicId → internal Id (T2 entity pattern) then filter branch status by long FK
        var internalId = await Ctx.Query<WorkflowInstance>()
            .Where(i => i.PublicId == instancePublicId)
            .Select(i => (long?)i.Id)
            .FirstOrDefaultAsync(ct);

        if (internalId is null) return null;

        return await Ctx.InstanceBranchStatuses
            .Include(s => s.ChildStatuses)
            .FirstOrDefaultAsync(
                s => s.InstanceId == internalId.Value
                    && s.Status == WorkflowInstanceBranchStatus.Statuses.InProgress,
                ct);
    }

    /// <inheritdoc/>
    public async Task<WorkflowInstanceBranchStatus?> GetBranchStatusByChildStatusIdAsync(
        long childStatusInternalId,
        CancellationToken ct = default)
    {
        return await Ctx.InstanceBranchStatuses
            .Include(s => s.ChildStatuses)
            .FirstOrDefaultAsync(
                s => s.ChildStatuses.Any(cs => cs.Id == childStatusInternalId),
                ct);
    }
}
