using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetTasksByAssignee;

public sealed class GetTasksByAssigneeQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetTasksByAssigneeQuery, Result<IReadOnlyList<WorkflowTaskDto>>>
{
    public async Task<Result<IReadOnlyList<WorkflowTaskDto>>> Handle(
        GetTasksByAssigneeQuery request,
        CancellationToken cancellationToken)
    {
        // Phase-07 PR-D2: JOIN WorkflowInstances to project wi.PublicId (Guid AK)
        // instead of the raw bigint FK (t.WorkflowInstanceId).
        const string sql = """
            SELECT t.Id, wi.PublicId AS WorkflowInstanceId, t.Title, t.Description, t.AssigneeId,
                   t.Status, t.Priority, t.DueDate, t.CompletedAt, t.CompletedBy,
                   t.TenantId, t.CreatedAt, t.CreatedBy
            FROM workflow.WorkflowTasks t
            INNER JOIN workflow.WorkflowInstances wi ON wi.Id = t.WorkflowInstanceId
            WHERE t.AssigneeId = @AssigneeId
              AND t.TenantId = @TenantId
              AND t.Status IN (0, 1)
              AND t.IsDeleted = 0
            ORDER BY t.Priority DESC, t.DueDate ASC
            """;

        var rows = await db.QueryAsync<TaskRow>(
            sql, new { request.AssigneeId, request.TenantId }, cancellationToken);

        var result = rows
            .Select(r => new WorkflowTaskDto(
                r.Id, r.WorkflowInstanceId, r.Title, r.Description,
                r.AssigneeId, (WorkflowTaskStatus)r.Status,
                (WorkflowTaskPriority)r.Priority,
                r.DueDate, r.CompletedAt, r.CompletedBy,
                r.TenantId, r.CreatedAt, r.CreatedBy))
            .ToList();

        return Result.Ok<IReadOnlyList<WorkflowTaskDto>>(result);
    }

    // WorkflowTask is T1: Id (long), WorkflowInstanceId now Guid (T2 PublicId AK via JOIN)
    internal sealed class TaskRow
    {
        public long Id { get; set; }
        public Guid WorkflowInstanceId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid? AssigneeId { get; set; }
        public int Status { get; set; }
        public int Priority { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? CompletedAt { get; set; }
        public Guid? CompletedBy { get; set; }
        public Guid TenantId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
    }
}
