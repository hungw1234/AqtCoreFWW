using AqtCoreFW.Rules.Application.Commands.ActivateRuleVersion;
using AqtCoreFW.Rules.Application.Commands.CreateRuleSet;
using AqtCoreFW.Rules.Application.Commands.UpdateRule;
using AqtCoreFW.Rules.Application.Queries.GetRuleSet;
using AqtCoreFW.Rules.Application.Queries.ListDecisionTablesByRuleSet;
using AqtCoreFW.Rules.Application.Queries.ListRuleSets;
using AqtCoreFW.Rules.Domain.Enums;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Rules.Presentation.Controllers;

/// <summary>
/// RuleSet CRUD and version activation endpoints.
/// TenantId resolved from JWT claim via ResolveTenantId() — not from caller-supplied params.
/// </summary>
[Route("api/v1/rules/rule-sets")]
[Authorize]
public sealed class RuleSetsController(ISender mediator)
    : ApiControllerBase
{
    /// <summary>List rule sets with optional search and pagination.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(
            new ListRuleSetsQuery(ResolveTenantId(), search, pageNumber, pageSize),
            cancellationToken));
    }

    /// <summary>Create a new Draft RuleSet.</summary>
    [HttpPost]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateRuleSetBody body,
        CancellationToken cancellationToken)
    {
        var tenantId = ResolveTenantId();
        return ToApiResponse(await mediator.Send(
            new CreateRuleSetCommand(tenantId, body.Name, body.Description, body.Category, body.TargetEntityType),
            cancellationToken));
    }

    /// <summary>Get RuleSet detail including all rules.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        return ToApiResponse(await mediator.Send(
            new GetRuleSetQuery(id, ResolveTenantId()), cancellationToken));
    }

    /// <summary>List decision tables belonging to a RuleSet.</summary>
    [HttpGet("{id:guid}/decision-tables")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> ListDecisionTables(
        Guid id,
        CancellationToken cancellationToken)
    {
        return ToApiResponse(await mediator.Send(
            new ListDecisionTablesByRuleSetQuery(id, ResolveTenantId()), cancellationToken));
    }

    /// <summary>Update a single rule within a RuleSet.</summary>
    [HttpPut("{id:guid}/rules/{ruleId:guid}")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> UpdateRule(
        Guid id,
        Guid ruleId,
        [FromBody] UpdateRuleBody body,
        CancellationToken cancellationToken) =>
        ToApiResponse(await mediator.Send(
            new UpdateRuleCommand(id, ruleId, body.Name, body.Expression,
                body.Priority, body.Enabled, body.FailureAction),
            cancellationToken));

    /// <summary>Snapshot current rules as a new version and activate the RuleSet.</summary>
    [HttpPost("{id:guid}/activate")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Activate(
        Guid id,
        CancellationToken cancellationToken) =>
        ToApiResponse(await mediator.Send(
            new ActivateRuleVersionCommand(id), cancellationToken));
}

// Request body records — separate from command types (GOV_SEC_004 mass-assignment prevention)
public sealed record CreateRuleSetBody(
    string Name,
    string? Description,
    string? Category,
    string? TargetEntityType);

public sealed record UpdateRuleBody(
    string? Name,
    string? Expression,
    int? Priority,
    bool? Enabled,
    FailureAction? FailureAction);
