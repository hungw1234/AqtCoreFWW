using AqtCoreFW.Audit.Application.Commands.UpdateClassificationRule;
using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.Audit.Application.Queries.GetClassificationInventory;
using AqtCoreFW.Audit.Application.Queries.GetClassificationRules;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Audit.Presentation.Controllers;

/// <summary>
/// QĐ 742 cấp 4 §4.4 — admin endpoints for data classification inventory and handling rules.
/// All endpoints require MFA per Phase-03 admin controller sweep.
/// </summary>
[Route("api/v1/admin/compliance")]
[Authorize(Policy = PermCodes.Compliance.View)]
[RequiresMfa]
public sealed class DataClassificationController(ISender mediator) : ApiControllerBase
{
    /// <summary>Scan all loaded domain assemblies and return classified field inventory.</summary>
    [HttpGet("classifications")]
    [ProducesResponseType(typeof(ClassificationInventoryDto), 200)]
    public async Task<IActionResult> GetInventory(CancellationToken cancellationToken) =>
        ToApiResponse(await mediator.Send(new GetClassificationInventoryQuery(), cancellationToken));

    /// <summary>Return the 4 data-level handling rules (ordered by Level).</summary>
    [HttpGet("classification-rules")]
    [ProducesResponseType(typeof(IReadOnlyList<ClassificationRuleDto>), 200)]
    public async Task<IActionResult> GetRules(CancellationToken cancellationToken) =>
        ToApiResponse(await mediator.Send(new GetClassificationRulesQuery(), cancellationToken));

    /// <summary>Update handling rules for a single classification level. Requires RowVersion for concurrency safety.</summary>
    [HttpPut("classification-rules/{level:int}")]
    [Authorize(Policy = PermCodes.Compliance.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> UpdateRule(
        int level,
        [FromBody] UpdateClassificationRuleRequest body,
        CancellationToken cancellationToken) =>
        ToApiResponse(await mediator.Send(
            new UpdateClassificationRuleCommand(
                (DataClassificationLevel)level,
                body.MustEncryptAtRest,
                body.AllowExport,
                body.MaxRetentionDays,
                body.RowVersion),
            cancellationToken));
}

public sealed record UpdateClassificationRuleRequest(
    bool MustEncryptAtRest,
    bool AllowExport,
    int? MaxRetentionDays,
    string RowVersion);
