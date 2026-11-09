using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetBranchStatus;

/// <summary>
/// Returns branch statuses for a workflow instance using two Dapper queries:
/// one for the branch status rows, one for all child statuses keyed by branch status ID.
/// Phase-4b B4c: WorkflowInstanceBranchStatus is T1 (long Id, long InstanceId FK, long ParallelBranchId FK).
///               WorkflowBranchChildStatus is T1 (long Id, long BranchChildId FK).
///               Filter by instance PublicId (Guid AK) via JOIN — internal long Id not exposed externally.
/// </summary>
public sealed class GetBranchStatusQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetBranchStatusQuery, Result<IReadOnlyList<WorkflowBranchStatusDto>>>
{
    public async Task<Result<IReadOnlyList<WorkflowBranchStatusDto>>> Handle(
        GetBranchStatusQuery request,
        CancellationToken cancellationToken)
    {
        // Tenant isolation: join to instance via PublicId (Guid AK).
        // bs.InstanceId is long (FK to T2 WorkflowInstance.Id internal bigint PK).
        const string branchSql = """
            SELECT bs.Id, bs.InstanceId, bs.ParallelBranchId, bs.Status, bs.ResolvedAt
            FROM workflow.WorkflowInstanceBranchStatuses bs
            INNER JOIN workflow.WorkflowInstances wi ON wi.Id = bs.InstanceId
            WHERE wi.PublicId = @InstanceId AND wi.TenantId = @TenantId
            ORDER BY bs.Id
            """;

        const string childSql = """
            SELECT cs.Id, cs.BranchChildId, cs.ResolutionType, cs.ResolvedBy,
                   cs.ResolvedAt, cs.Comment, cs.InstanceBranchStatusId,
                   childTask.AssigneeId, childTask.TaskId
            FROM workflow.WorkflowBranchChildStatuses cs
            INNER JOIN workflow.WorkflowInstanceBranchStatuses bs
                ON bs.Id = cs.InstanceBranchStatusId
            INNER JOIN workflow.WorkflowInstances wi ON wi.Id = bs.InstanceId
            OUTER APPLY (
                SELECT TOP 1 wt.Id AS TaskId, wt.AssigneeId
                FROM workflow.WorkflowTasks wt
                WHERE wt.BranchChildStatusId = cs.Id AND wt.Status <> @CancelledStatus
                ORDER BY wt.CreatedAt DESC
            ) childTask
            WHERE wi.PublicId = @InstanceId AND wi.TenantId = @TenantId
            """;

        var branchRows = (await db.QueryAsync<BranchRow>(
            branchSql, new { request.InstanceId, request.TenantId }, cancellationToken)).ToList();

        if (branchRows.Count == 0)
            return Result.Ok<IReadOnlyList<WorkflowBranchStatusDto>>([]);

        var childRows = (await db.QueryAsync<ChildRow>(
            childSql, new { request.InstanceId, request.TenantId, CancelledStatus = 3 }, cancellationToken)).ToList();

        // Group children by parent branch status ID (long)
        var childrenByBranch = childRows
            .GroupBy(c => c.InstanceBranchStatusId)
            .ToDictionary(g => g.Key, g => g.ToList());

        IReadOnlyList<WorkflowBranchStatusDto> result = branchRows
            .Select(b => new WorkflowBranchStatusDto(
                b.Id,               // long (T1 PK)
                b.InstanceId,       // long (T1 FK)
                b.ParallelBranchId, // long (T1 FK)
                b.Status,
                b.ResolvedAt,
                childrenByBranch.TryGetValue(b.Id, out var children)
                    ? children.Select(c => new WorkflowBranchChildStatusDto(
                        c.Id,           // long (T1 PK)
                        c.BranchChildId, // long (T1 FK)
                        c.ResolutionType,
                        c.ResolvedBy, c.ResolvedAt, c.Comment, c.AssigneeId, c.TaskId)).ToList()
                    : []))
            .ToList();

        return Result.Ok(result);
    }

    // WorkflowInstanceBranchStatus is T1: all structural IDs are long.
    private sealed class BranchRow
    {
        public long Id { get; set; }
        public long InstanceId { get; set; }
        public long ParallelBranchId { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset? ResolvedAt { get; set; }
    }

    // WorkflowBranchChildStatus is T1: Id (long), BranchChildId (long FK), InstanceBranchStatusId (long FK).
    // ResolvedBy/AssigneeId are Guid (FK exception — user refs).
    private sealed class ChildRow
    {
        public long Id { get; set; }
        public long BranchChildId { get; set; }
        public int? ResolutionType { get; set; }
        public Guid? ResolvedBy { get; set; }
        public DateTimeOffset? ResolvedAt { get; set; }
        public string? Comment { get; set; }
        public long InstanceBranchStatusId { get; set; }
        public Guid? AssigneeId { get; set; }
        public long? TaskId { get; set; }
    }
}
