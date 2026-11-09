using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using AqtCoreFW.Workflow.Application.Commands.AssignTask;
using AqtCoreFW.Workflow.Application.Commands.CancelTask;
using AqtCoreFW.Workflow.Application.Commands.ClaimTask;
using AqtCoreFW.Workflow.Application.Commands.CompleteTask;
using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowTask;
using AqtCoreFW.Workflow.Application.Commands.ReassignTask;
using AqtCoreFW.Workflow.Application.Commands.UnclaimTask;
using AqtCoreFW.Workflow.Application.Queries.GetEscalationRules;
using AqtCoreFW.Workflow.Application.Queries.GetInbox;
using AqtCoreFW.Workflow.Application.Queries.GetTasksByAssignee;
using AqtCoreFW.Workflow.Application.Queries.GetTasksByInstance;
using AqtCoreFW.Workflow.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Workflow.Presentation.Controllers;

/// <summary>
/// Workflow Tasks REST API — CRUD + lifecycle actions (assign, reassign, complete, cancel).
/// Tenant ID and actor IDs are always extracted from JWT claims, never from request body.
/// </summary>
[Route("api/v1/workflow/tasks")]
[Authorize]
public sealed class WorkflowTasksController(ISender mediator) : ApiControllerBase
{
    /// <summary>
    /// Get the caller's inbox: open/in-progress tasks assigned to the caller OR in a pool the
    /// caller can claim (EXISTS on WorkflowTaskCandidateIndex with PrincipalType ∈ {Permission, Role};
    /// Group/OrgUnit deferred — RT2-15 Phase 02/04).
    /// Role membership is read direct-only from AspNetUserRoles (group-inherited excluded —
    /// PQ-1 A semantic, T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION).
    /// UserId and TenantId are resolved from JWT claims — no request body needed.
    /// Returns 403 for authenticated callers lacking Workflow.Task.Process permission.
    /// </summary>
    /// <remarks>
    /// 4-A-A INVARIANT: explicit Policy= required — class-level [Authorize] is NOT a security gate.
    /// P4 Slice 4-C.
    /// </remarks>
    [HttpGet("inbox")]
    [Authorize(Policy = PermCodes.Workflow.Task.Process)]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetInbox(
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetInboxQuery(ResolveUserId(), ResolveTenantId()),
            cancellationToken));

    /// <summary>Get all tasks for a workflow instance (with assignee name + candidate pool).</summary>
    /// <remarks>
    /// Explicit BP.VIEW policy — the response exposes the candidate pool and resolved assignee
    /// names, so it is gated to BP participants (same gate as the BP-run detail page that calls it).
    /// Class-level [Authorize] alone is NOT a sufficient gate (4-A-A invariant).
    /// </remarks>
    [HttpGet("by-instance/{instanceId:guid}")]
    [Authorize(Policy = PermCodes.BusinessProcess.Bp.View)]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetByInstance(
        Guid instanceId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetTasksByInstanceQuery(instanceId, ResolveTenantId()), cancellationToken));

    /// <summary>Get open/in-progress tasks assigned to a user.</summary>
    [HttpGet("by-assignee/{assigneeId:guid}")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetByAssignee(
        Guid assigneeId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetTasksByAssigneeQuery(assigneeId, ResolveTenantId()), cancellationToken));

    /// <summary>Create a new task within a workflow instance.</summary>
    [HttpPost]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateWorkflowTaskRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new CreateWorkflowTaskCommand(
                request.WorkflowInstancePublicId,
                request.Title,
                request.Description,
                request.AssigneeId,
                request.Priority,
                request.DueDate,
                ResolveTenantId(),
                ResolveUserId()),
            cancellationToken));

    /// <summary>Assign a task to a user (initial assignment). Requires Workflow.Assign.Manage permission.</summary>
    /// <remarks>
    /// Phase-4-A IDOR fix: elevated from inherited [Authorize] to [Authorize(Policy = Workflow.Assign.Manage)].
    /// Same bug class as sibling /reassign — initial-assign is also an admin action and must not be exposed to any authenticated user.
    /// </remarks>
    [HttpPost("{id:long}/assign")]
    [Authorize(Policy = PermCodes.Workflow.Assign.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Assign(
        long id,
        [FromBody] AssignTaskRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new AssignTaskCommand(id, request.AssigneeId, ResolveUserId(), request.Reason, ResolveTenantId()),
            cancellationToken));

    /// <summary>Reassign a task to a different user. Requires Workflow.Assign.Manage permission.</summary>
    /// <remarks>
    /// Phase-4-A IDOR fix: elevated from plain [Authorize] to [Authorize(Policy = Workflow.Assign.Manage)].
    /// When the task has an existing assignee (claimed), Reason is mandatory — enforced in handler.
    /// Phase-4b B6a: id is long (T1 WorkflowTask.Id bigint PK).
    /// </remarks>
    [HttpPost("{id:long}/reassign")]
    [Authorize(Policy = PermCodes.Workflow.Assign.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Reassign(
        long id,
        [FromBody] ReassignTaskRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ReassignTaskCommand(id, request.NewAssigneeId, ResolveUserId(), request.Reason, ResolveTenantId()),
            cancellationToken));

    /// <summary>
    /// Claim a pool task (self-service). Requires Workflow.Task.Process permission.
    /// Returns 409 when already claimed (sequential guard or concurrent RowVersion race).
    /// Returns 403 when caller is not pool-eligible (passes coarse policy but fails handler pool check).
    /// </summary>
    /// <remarks>
    /// Q1 (user-ratified): reuses Workflow.Task.Process — same as ExecuteTransition coarse gate.
    /// No request body — ActorId and TenantId extracted from JWT claims.
    /// P4 Slice 4-B.
    /// </remarks>
    [HttpPost("{id:long}/claim")]
    [Authorize(Policy = PermCodes.Workflow.Task.Process)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Claim(
        long id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ClaimTaskCommand(id, ResolveUserId(), ResolveTenantId()),
            cancellationToken));

    /// <summary>
    /// Unclaim a pool task (self-service). Requires Workflow.Task.Process permission.
    /// Only the current assignee may unclaim. Idempotent — returns 200 if already unclaimed.
    /// Returns 403 when caller is not the current assignee.
    /// </summary>
    /// <remarks>
    /// Q1 (user-ratified): reuses Workflow.Task.Process — same as Claim.
    /// No request body — ActorId and TenantId extracted from JWT claims.
    /// P4 Slice 4-B.
    /// </remarks>
    [HttpPost("{id:long}/unclaim")]
    [Authorize(Policy = PermCodes.Workflow.Task.Process)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Unclaim(
        long id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new UnclaimTaskCommand(id, ResolveUserId(), ResolveTenantId()),
            cancellationToken));

    /// <summary>Mark a task as complete.</summary>
    /// <remarks>Phase-4b B6a: id is long (T1 WorkflowTask.Id bigint PK).</remarks>
    [HttpPost("{id:long}/complete")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Complete(
        long id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new CompleteTaskCommand(id, ResolveUserId(), ResolveTenantId()),
            cancellationToken));

    /// <summary>Cancel a task with an optional reason.</summary>
    /// <remarks>Phase-4b B6a: id is long (T1 WorkflowTask.Id bigint PK).</remarks>
    [HttpPost("{id:long}/cancel")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Cancel(
        long id,
        [FromBody] CancelTaskRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new CancelTaskCommand(id, request.Reason, ResolveUserId(), ResolveTenantId()),
            cancellationToken));

    /// <summary>Get escalation rules for a workflow definition.</summary>
    [HttpGet("escalation-rules")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetEscalationRules(
        [FromQuery] Guid workflowDefinitionId,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetEscalationRulesQuery(workflowDefinitionId, ResolveTenantId(), isActive),
            cancellationToken));
}

// Request body records — actor IDs and tenant ID removed (extracted from JWT, not request body)
public sealed record CreateWorkflowTaskRequest(
    Guid WorkflowInstancePublicId, // Phase-07 PR-2: Guid (T2 WorkflowInstance external PublicId AK; handler resolves to long FK)
    string Title,
    string? Description,
    Guid? AssigneeId,
    WorkflowTaskPriority Priority,
    DateTime? DueDate);

public sealed record AssignTaskRequest(
    Guid AssigneeId,
    string? Reason);

public sealed record ReassignTaskRequest(
    Guid NewAssigneeId,
    string? Reason);

public sealed record CancelTaskRequest(
    string? Reason);
