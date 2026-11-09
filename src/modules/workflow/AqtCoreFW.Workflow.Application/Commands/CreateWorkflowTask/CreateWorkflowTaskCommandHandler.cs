using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CreateWorkflowTask;

/// <summary>
/// Creates a workflow task and auto-delegates if the assignee has an active delegation rule.
/// Step 12: After task creation, checks IDelegationRuleRepository.GetActiveRuleAsync —
/// if a rule is found and current time is within [ValidFrom, ValidUntil], delegates the task
/// and records a TaskAssignment(Type=Delegate) before saving.
/// Phase-07 PR-2: Resolves WorkflowInstancePublicId (Guid T2 AK) → internal long Id via IRepository&lt;T, Guid&gt;.GetByIdAsync.
/// IRepository&lt;WorkflowInstance, Guid&gt; key is PublicId for T2 — GetByIdAsync(Guid) = lookup by PublicId.
/// </summary>
public sealed class CreateWorkflowTaskCommandHandler(
    IWorkflowTaskRepository repository,
    IWorkflowInstanceRepository instanceRepository,
    ITaskAssignmentRepository? assignmentRepository = null,
    IDelegationRuleRepository? delegationRuleRepository = null)
    : IRequestHandler<CreateWorkflowTaskCommand, Result<WorkflowTaskDto>>
{
    public async Task<Result<WorkflowTaskDto>> Handle(
        CreateWorkflowTaskCommand request,
        CancellationToken cancellationToken)
    {
        // Resolve T2 PublicId (Guid AK) → T2 internal Id (long PK).
        // IRepository<WorkflowInstance, Guid> is keyed on PublicId — no new repo method needed.
        var lookup = await instanceRepository.GetByIdAsync(request.WorkflowInstancePublicId, cancellationToken);
        if (lookup.IsFailed)
            return Result.Fail<WorkflowTaskDto>(lookup.Errors);

        // WorkflowTask.Create takes long workflowInstanceId (T1 FK to T2 internal bigint PK)
        var task = WorkflowTask.Create(
            lookup.Value.Id,
            request.Title,
            request.Description,
            request.AssigneeId,
            request.Priority,
            request.DueDate,
            request.TenantId,
            request.CreatedBy);

        // Step 12: auto-delegate if assignee has an active delegation rule
        if (request.AssigneeId.HasValue
            && delegationRuleRepository is not null
            && assignmentRepository is not null)
        {
            var rule = await delegationRuleRepository.GetActiveRuleAsync(
                request.AssigneeId.Value,
                request.TenantId,
                cancellationToken);

            if (rule is not null)
            {
                task.Delegate(request.AssigneeId.Value, rule.DelegateUserId, request.CreatedBy);

                // TaskAssignment.Create takes long workflowTaskId (T1 FK)
                var assignment = TaskAssignment.Create(
                    task.Id,
                    request.CreatedBy,
                    rule.DelegateUserId,
                    TaskAssignmentType.Delegate,
                    "auto-delegate");

                await assignmentRepository.AddAsync(assignment, cancellationToken);
            }
        }

        await repository.AddAsync(task, cancellationToken);

        return Result.Ok(MapToDto(task, lookup.Value.PublicId));
    }

    /// <summary>
    /// Maps domain entity to DTO.
    /// instancePublicId passed by caller (already resolved) — entity only stores internal long FK.
    /// Phase-07 PR-D2: WorkflowTaskDto.WorkflowInstanceId is now Guid (T2 PublicId AK).
    /// </summary>
    internal static WorkflowTaskDto MapToDto(WorkflowTask t, Guid instancePublicId) => new(
        t.Id,           // long (T1 PK)
        instancePublicId, // Guid (T2 WorkflowInstance.PublicId AK)
        t.Title, t.Description,
        t.AssigneeId, t.Status, t.Priority, t.DueDate,
        t.CompletedAt, t.CompletedBy, t.TenantId, t.CreatedAt, t.CreatedBy,
        t.DelegatedFromUserId);
}
