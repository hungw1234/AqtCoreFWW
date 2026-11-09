using AqtCoreFW.Rules.Application.Commands.CreateDecisionTable;
using AqtCoreFW.Rules.Application.Queries.GetDecisionTable;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Rules.Presentation.Controllers;

/// <summary>
/// Decision table CRUD endpoints.
/// TenantId resolved from JWT claim via ResolveTenantId() — not from caller-supplied params.
/// </summary>
[Route("api/v1/decision-tables")]
[Authorize]
public sealed class DecisionTablesController(ISender mediator)
    : ApiControllerBase
{
    /// <summary>Create a new decision table linked to a RuleSet.</summary>
    [HttpPost]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateDecisionTableBody body,
        CancellationToken cancellationToken)
    {
        return ToApiResponse(await mediator.Send(
            new CreateDecisionTableCommand(
                body.RuleSetId, ResolveTenantId(), body.Name,
                body.InputColumns, body.OutputColumns, body.Rows),
            cancellationToken));
    }

    /// <summary>Get decision table by ID. T1 entity → route param is long internal PK.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        return ToApiResponse(await mediator.Send(
            new GetDecisionTableQuery(id, ResolveTenantId()), cancellationToken));
    }
}

public sealed record CreateDecisionTableBody(
    Guid RuleSetId,
    string Name,
    string InputColumns,
    string OutputColumns,
    string Rows);
