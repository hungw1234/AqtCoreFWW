namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>
/// Read projection of WorkflowInstanceBranchStatus with nested child statuses.
/// Phase-4b B4c: T1 entity — Id, InstanceId, ParallelBranchId are long (bigint identity PKs).
/// </summary>
public sealed record WorkflowBranchStatusDto(
    long Id,
    long InstanceId,
    long ParallelBranchId,
    string Status,
    DateTimeOffset? ResolvedAt,
    IReadOnlyList<WorkflowBranchChildStatusDto> ChildStatuses);

/// <summary>
/// Read projection of WorkflowBranchChildStatus with linked task assignee + task id.
/// Phase-4b B4c: T1 entity — Id and BranchChildId are long (bigint identity PKs).
/// TaskId is the live (non-cancelled) WorkflowTask.Id for this child — null until the
/// fork stages the task. The FE needs it to drive pool Claim (POST tasks/{id}/claim).
/// </summary>
public sealed record WorkflowBranchChildStatusDto(
    long Id,
    long BranchChildId,
    int? ResolutionType,
    Guid? ResolvedBy,
    DateTimeOffset? ResolvedAt,
    string? Comment,
    Guid? AssigneeId,
    long? TaskId);
