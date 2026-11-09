using AqtCoreFW.Rules.Application.Commands.RunTestCase;
using AqtCoreFW.Rules.Application.Queries.EvaluateRules;
using AqtCoreFW.Rules.Application.Queries.ExplainRules;
using AqtCoreFW.Rules.Application.Queries.SimulateRules;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;

namespace AqtCoreFW.Rules.Presentation.Controllers;

/// <summary>
/// Rule evaluation endpoints — evaluate, simulate (no audit), explain (full trace), run test cases.
/// </summary>
[Route("api/v1/rules")]
[Authorize]
public sealed class RuleEvaluationController(ISender mediator, ITenantContext tenantContext)
    : ApiControllerBase
{
    /// <summary>Evaluate a RuleSet against JSON input. Publishes audit event.</summary>
    [HttpPost("evaluate")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Evaluate(
        [FromBody] EvaluateRulesBody body,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? body.TenantId ?? Guid.Empty;
        using var inputDoc = JsonDocument.Parse(body.InputJson);
        return ToApiResponse(await mediator.Send(
            new EvaluateRulesQuery(body.RuleSetId, tenantId, inputDoc), cancellationToken));
    }

    /// <summary>Simulate evaluation — same as evaluate but no audit event, returns trace.</summary>
    [HttpPost("simulate")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Simulate(
        [FromBody] EvaluateRulesBody body,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? body.TenantId ?? Guid.Empty;
        using var inputDoc = JsonDocument.Parse(body.InputJson);
        return ToApiResponse(await mediator.Send(
            new SimulateRulesQuery(body.RuleSetId, tenantId, inputDoc), cancellationToken));
    }

    /// <summary>Explain which rules fired and why — detailed trace JSON.</summary>
    [HttpPost("{ruleSetId:guid}/explain")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Explain(
        Guid ruleSetId,
        [FromBody] ExplainBody body,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? body.TenantId ?? Guid.Empty;
        using var inputDoc = JsonDocument.Parse(body.InputJson);
        return ToApiResponse(await mediator.Send(
            new ExplainRulesQuery(ruleSetId, tenantId, inputDoc), cancellationToken));
    }

    /// <summary>Run a saved test case — evaluates and records result. T1 entity → long Id.</summary>
    [HttpPost("test-cases/{id:long}/run")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RunTestCase(
        long id,
        CancellationToken cancellationToken) =>
        ToApiResponse(await mediator.Send(
            new RunTestCaseCommand(id), cancellationToken));
}

public sealed record EvaluateRulesBody(Guid RuleSetId, Guid? TenantId, string InputJson);
public sealed record ExplainBody(Guid? TenantId, string InputJson);
