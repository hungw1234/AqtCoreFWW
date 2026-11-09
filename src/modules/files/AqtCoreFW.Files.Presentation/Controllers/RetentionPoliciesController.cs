using AqtCoreFW.Files.Application.Commands.CreateRetentionPolicy;
using AqtCoreFW.Files.Application.Queries.GetRetentionPolicies;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Files.Presentation.Controllers;

/// <summary>
/// Retention Policies REST API â€” create and list policies.
/// Policies are tenant-scoped; management requires Admin role.
/// TenantId and user identity are resolved from JWT â€” never accepted from clients.
/// </summary>
[Route("api/v1/retention-policies")]
[Authorize]
public sealed class RetentionPoliciesController(ISender mediator) : ApiControllerBase
{
    /// <summary>List retention policies for the resolved tenant.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetRetentionPoliciesQuery(ResolveTenantId(), isActive),
            cancellationToken));

    /// <summary>Create a new retention policy.</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Files.Retention.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateRetentionPolicyRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new CreateRetentionPolicyCommand(
                request.Name,
                request.DocumentType,
                request.RetainDays,
                request.ArchiveAfterDays,
                request.DestroyAfterDays,
                ResolveTenantId(),
                ResolveUserId()),
            cancellationToken));
}

public sealed record CreateRetentionPolicyRequest(
    string Name,
    string DocumentType,
    int RetainDays,
    int? ArchiveAfterDays,
    int? DestroyAfterDays);
