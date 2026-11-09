using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using AqtCoreFW.Workflow.Application.Commands.CreateAssignmentRule;
using AqtCoreFW.Workflow.Application.Commands.DeleteAssignmentRule;
using AqtCoreFW.Workflow.Application.Queries.GetAssignmentRules;
using AqtCoreFW.Workflow.Application.Queries.ResolveWorkflowAssignment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Workflow.Presentation.Controllers;

/// <summary>
/// Workflow Assignment Rules REST API.
/// Manages tenant-to-workflow-definition mapping with 4-level specificity.
/// All operations restricted to admins holding Workflow.Assign.Manage — rules affect all instances for that tenant.
/// Tenant ID and user identity resolved from JWT claims, not request body.
/// </summary>
/// <remarks>
/// 4-A-A INVARIANT: explicit Policy= required on every action — class-level [Authorize] is NOT a security gate.
/// Phase-04-A follow-up 2026-05-22: List + Resolve elevated from naked [Authorize] to Workflow.Assign.Manage
/// (same bug class as 4-A WorkflowTasksController.Reassign).
/// </remarks>
[Route("api/v1/workflow/assignments")]
[Authorize]
public sealed class WorkflowAssignmentsController(ISender mediator) : ApiControllerBase
{
    /// <summary>List all assignment rules for a tenant, ordered by specificity and priority.</summary>
    [HttpGet]
    [Authorize(Policy = PermCodes.Workflow.Assign.Manage)]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> List(
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new GetAssignmentRulesQuery(ResolveTenantId()), ct));

    /// <summary>Create a new assignment rule for a tenant + entity scope.</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Workflow.Assign.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAssignmentRuleRequest request,
        CancellationToken ct = default)
    {
        var command = new CreateAssignmentRuleCommand(
            ResolveTenantId(),
            request.WorkflowDefinitionId,
            request.EntityType,
            request.EntitySubType,
            request.Priority,
            request.Description,
            ResolveUserId(),
            request.EffectiveFrom,
            request.EffectiveUntil);
        return ToApiResponse(await mediator.Send(command, ct));
    }

    /// <summary>Deactivate (soft-delete) an assignment rule. Addressed by internal long Id (T1 entity, no PublicId).</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Policy = PermCodes.Workflow.Assign.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(
        long id,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new DeleteAssignmentRuleCommand(id, ResolveTenantId(), ResolveUserId()), ct));

    /// <summary>
    /// Resolve the best-matching WorkflowDefinitionId for a given tenant + entity type.
    /// Uses cache-aside (24h TTL) — returns 4-level specificity winner.
    /// </summary>
    [HttpGet("resolve")]
    [Authorize(Policy = PermCodes.Workflow.Assign.Manage)]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Resolve(
        [FromQuery] string entityType,
        [FromQuery] string? entitySubType = null,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new ResolveWorkflowAssignmentQuery(ResolveTenantId(), entityType, entitySubType), ct));
}

// Request body record â€" TenantId and CreatedBy removed (resolved from JWT)
public sealed record CreateAssignmentRuleRequest(
    Guid WorkflowDefinitionId,
    string? EntityType,
    string? EntitySubType,
    int Priority,
    string? Description,
    DateTimeOffset? EffectiveFrom = null,
    DateTimeOffset? EffectiveUntil = null);
