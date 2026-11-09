using AqtCoreFW.Workflow.Domain.Entities;

namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>
/// DTO returned for WorkflowTask read operations.
/// Phase-4b B4c: T1 entity — Id is long (bigint identity PK).
/// Phase-07 PR-D2: WorkflowInstanceId is Guid (T2 WorkflowInstance.PublicId AK).
/// </summary>
public sealed record WorkflowTaskDto(
    long Id,
    Guid WorkflowInstanceId,
    string Title,
    string? Description,
    Guid? AssigneeId,
    WorkflowTaskStatus Status,
    WorkflowTaskPriority Priority,
    DateTime? DueDate,
    DateTime? CompletedAt,
    Guid? CompletedBy,
    Guid TenantId,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    /// <summary>Non-null means the task is currently delegated; holds original assignee Id.</summary>
    Guid? DelegatedFromUserId = null);

/// <summary>
/// Task projection for the instance detail page — carries the resolved assignee display
/// name and the candidate pool so the UI can answer "who handles this next" without extra
/// round-trips. Distinct from <see cref="WorkflowTaskDto"/> (the shared inbox/delegation DTO)
/// to keep that perf-sensitive contract free of detail-page concerns.
/// </summary>
public sealed record WorkflowInstanceTaskDto(
    long Id,
    Guid WorkflowInstanceId,
    string Title,
    WorkflowTaskStatus Status,
    WorkflowTaskPriority Priority,
    DateTime? DueDate,
    Guid? AssigneeId,
    string? AssigneeName,
    IReadOnlyList<WorkflowTaskCandidateDto> Candidates,
    /// <summary>Up to a capped number of users eligible to claim a pool task (Role/Group/OrgUnit only).</summary>
    IReadOnlyList<WorkflowEligibleUserDto> EligibleUsers,
    /// <summary>Total distinct eligible users from resolved (Role/Group/OrgUnit) candidates — may exceed EligibleUsers.Count.</summary>
    int EligibleUserCount,
    /// <summary>True when the task also has a Permission candidate whose users are NOT resolved — EligibleUsers/Count are then INCOMPLETE.</summary>
    bool HasUnresolvedPermissionCandidate);

/// <summary>
/// One candidate principal of an unassigned (pool) task. PrincipalType: 0=Permission,
/// 1=Role, 2=Group, 3=OrgUnit. PrincipalRefId is the canonical ref (role NormalizedName,
/// or group/org-unit/permission code) — the FE resolves it to a display label client-side.
/// </summary>
public sealed record WorkflowTaskCandidateDto(
    int PrincipalType,
    string PrincipalRefId);

/// <summary>A user eligible to claim a pool task (resolved from Role/Group/OrgUnit candidates).</summary>
public sealed record WorkflowEligibleUserDto(
    Guid UserId,
    string? FullName);

/// <summary>
/// DTO for TaskAssignment audit records.
/// Phase-4b B4c: T1 entity — Id and WorkflowTaskId are long (bigint identity PKs).
/// </summary>
public sealed record TaskAssignmentDto(
    long Id,
    long WorkflowTaskId,
    Guid AssignedById,
    Guid AssignedToId,
    DateTime AssignedAt,
    string? Reason,
    TaskAssignmentType Type);

/// <summary>
/// DTO for EscalationRule.
/// Phase-4b B4c: T1 entity — Id and StateId are long.
/// Phase-07 PR-D2: WorkflowDefinitionId is Guid (T2 WorkflowDefinition.PublicId AK).
/// </summary>
public sealed record EscalationRuleDto(
    long Id,
    Guid WorkflowDefinitionId,
    long? StateId,
    int EscalationAfterMinutes,
    EscalationTarget EscalateTo,
    string EscalateToValue,
    string NotifyVia,
    bool IsActive,
    Guid TenantId);
