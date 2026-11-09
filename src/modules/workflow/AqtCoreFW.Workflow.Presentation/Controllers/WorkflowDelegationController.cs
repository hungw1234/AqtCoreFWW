using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using AqtCoreFW.Workflow.Application.Commands.CreateDelegationRule;
using AqtCoreFW.Workflow.Application.Commands.DeleteDelegationRule;
using AqtCoreFW.Workflow.Application.Commands.DelegateTask;
using AqtCoreFW.Workflow.Application.Commands.ReturnDelegatedTask;
using AqtCoreFW.Workflow.Application.Commands.UpdateDelegationRule;
using AqtCoreFW.Workflow.Application.Queries.GetDelegationRules;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Workflow.Presentation.Controllers;

/// <summary>
/// Workflow Delegation REST API â€" task delegation and delegation rule CRUD.
/// Task delegate/return: task assignee or admin (runtime check in handler).
/// Delegation rules (CRUD): requires WORKFLOW.DELEGATION.MANAGE permission.
/// Tenant ID and actor IDs always resolved from JWT, never from request body.
/// IsAdminOverride checked via IAuthorizationService against WORKFLOW.DELEGATION.MANAGE policy
/// (uses IEffectivePermissionService under the hood â€" not claims-based).
/// </summary>
[Route("api/v1/workflow")]
[Authorize]
public sealed class WorkflowDelegationController(
    ISender mediator,
    IAuthorizationService authorizationService) : ApiControllerBase
{
    /// <summary>Delegate a task to another user with optional reason and expiry.</summary>
    /// <remarks>Phase-4b B6a: id is long (T1 WorkflowTask.Id bigint PK).</remarks>
    [HttpPost("tasks/{id:long}/delegate")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> DelegateTask(
        long id,
        [FromBody] DelegateTaskRequest request,
        CancellationToken ct = default)
    {
        var isAdmin = (await authorizationService.AuthorizeAsync(
            User, PermCodes.Workflow.Delegation.Manage)).Succeeded;

        return ToApiResponse(await mediator.Send(
            new DelegateTaskCommand(
                id, request.DelegateToUserId, request.ExpiresAt, request.Reason,
                ResolveUserId(), ResolveTenantId(),
                IsAdminOverride: isAdmin),
            ct));
    }

    /// <summary>Return a delegated task to its original assignee. Idempotent if not delegated.</summary>
    /// <remarks>Phase-4b B6a: id is long (T1 WorkflowTask.Id bigint PK).</remarks>
    [HttpPost("tasks/{id:long}/return-delegation")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ReturnDelegation(
        long id,
        CancellationToken ct = default)
    {
        var isAdmin = (await authorizationService.AuthorizeAsync(
            User, PermCodes.Workflow.Delegation.Manage)).Succeeded;

        return ToApiResponse(await mediator.Send(
            new ReturnDelegatedTaskCommand(
                id, ResolveUserId(), ResolveTenantId(),
                IsAdminOverride: isAdmin),
            ct));
    }

    /// <summary>List delegation rules for the current tenant. Optional filter by delegator user.</summary>
    /// <remarks>
    /// 4-A-A INVARIANT: explicit Policy= required — class-level [Authorize] is NOT a security gate.
    /// Phase-04-A follow-up 2026-05-22: elevated from naked [Authorize] to Workflow.Delegation.Manage
    /// (rule enumeration is admin-only; same bug class as 4-A WorkflowTasksController.Reassign).
    /// Task delegate/return-delegation routes (above) use runtime IAuthorizationService — intentional.
    /// </remarks>
    [HttpGet("delegation-rules")]
    [Authorize(Policy = PermCodes.Workflow.Delegation.Manage)]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? delegatorUserId = null,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new GetDelegationRulesQuery(ResolveTenantId(), delegatorUserId),
            ct));

    /// <summary>Create a delegation rule. Rejects if overlapping active rule exists.</summary>
    [HttpPost("delegation-rules")]
    [Authorize(Policy = PermCodes.Workflow.Delegation.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateDelegationRuleRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new CreateDelegationRuleCommand(
                request.DelegatorUserId, request.DelegateUserId,
                request.ValidFrom, request.ValidUntil,
                ResolveTenantId(), ResolveUserId()),
            ct));

    /// <summary>Update an existing delegation rule's window and/or delegate user.</summary>
    /// <remarks>Phase-4b B6a: id is long (T1 DelegationRule.Id bigint PK).</remarks>
    [HttpPut("delegation-rules/{id:long}")]
    [Authorize(Policy = PermCodes.Workflow.Delegation.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Update(
        long id,
        [FromBody] UpdateDelegationRuleRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new UpdateDelegationRuleCommand(
                id, request.DelegateUserId,
                request.ValidFrom, request.ValidUntil,
                ResolveUserId(), ResolveTenantId()),
            ct));

    /// <summary>Soft-delete a delegation rule (sets IsActive = false).</summary>
    /// <remarks>Phase-4b B6a: id is long (T1 DelegationRule.Id bigint PK).</remarks>
    [HttpDelete("delegation-rules/{id:long}")]
    [Authorize(Policy = PermCodes.Workflow.Delegation.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(
        long id,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new DeleteDelegationRuleCommand(id, ResolveUserId(), ResolveTenantId()),
            ct));
}

// Request body records â€" actor IDs and tenant ID resolved from JWT, not request body

public sealed record DelegateTaskRequest(
    Guid DelegateToUserId,
    DateTimeOffset ExpiresAt,
    string? Reason);

public sealed record CreateDelegationRuleRequest(
    Guid DelegatorUserId,
    Guid DelegateUserId,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil);

public sealed record UpdateDelegationRuleRequest(
    Guid DelegateUserId,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil);
