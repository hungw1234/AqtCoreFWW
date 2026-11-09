using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Commands.CreateJitProviderConfig;
using AqtCoreFW.Identity.Application.Commands.DeleteJitProviderConfig;
using AqtCoreFW.Identity.Application.Commands.UpdateJitProviderConfig;
using AqtCoreFW.Identity.Application.Queries.GetJitProviderConfigByScheme;
using AqtCoreFW.Identity.Application.Queries.ListJitProviderConfigs;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Admin endpoints for managing JIT SSO provider configurations.
/// Controls per-scheme JIT provisioning rules (roles, approval, domain allow-lists, rate limits).
/// Requires Admin policy.
/// </summary>
[Route("api/v1/admin/jit-provider-configs")]
[Authorize(Policy = PermCodes.System.Config.Manage)]
[RequiresMfa]
public sealed class JitProviderConfigsController(ISender mediator) : ApiControllerBase
{
    /// <summary>List all JIT provider configs (paginated).</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(
            new ListJitProviderConfigsQuery(page, pageSize), ct));
    }

    /// <summary>Get a single JIT provider config by scheme name.</summary>
    [HttpGet("{scheme}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetByScheme(string scheme, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new GetJitProviderConfigBySchemeQuery(scheme), ct));

    /// <summary>Create a new JIT provider config.</summary>
    [HttpPost]
    [ProducesResponseType(201)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateJitProviderConfigRequest request,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new CreateJitProviderConfigCommand(
                request.Scheme, request.DisplayName, request.ProviderType,
                request.JitEnabled, request.DefaultRoleName, request.RequireApproval,
                request.ClaimMappingJson, request.DefaultTenantId,
                request.AllowedDomainsJson, request.MaxProvisionsPerHour,
                GetActorId()), ct);

        if (result.IsSuccess)
            return CreatedAtAction(nameof(GetByScheme), new { scheme = request.Scheme }, null);

        return ToApiResponse(result);
    }

    /// <summary>Update an existing JIT provider config.</summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Update(
        long id,
        [FromBody] UpdateJitProviderConfigRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new UpdateJitProviderConfigCommand(
                id, request.DisplayName, request.JitEnabled, request.DefaultRoleName,
                request.RequireApproval, request.ClaimMappingJson, request.DefaultTenantId,
                request.AllowedDomainsJson, request.MaxProvisionsPerHour,
                GetActorId()), ct));

    /// <summary>Soft-deactivate a JIT provider config (does not delete the record).</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new DeleteJitProviderConfigCommand(id, GetActorId()), ct));

    private Guid GetActorId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

public sealed record CreateJitProviderConfigRequest(
    string Scheme,
    string DisplayName,
    ExternalIdentityProvider ProviderType,
    bool JitEnabled,
    string DefaultRoleName,
    bool RequireApproval,
    string? ClaimMappingJson,
    Guid? DefaultTenantId,
    string? AllowedDomainsJson,
    int MaxProvisionsPerHour);

public sealed record UpdateJitProviderConfigRequest(
    string DisplayName,
    bool JitEnabled,
    string DefaultRoleName,
    bool RequireApproval,
    string? ClaimMappingJson,
    Guid? DefaultTenantId,
    string? AllowedDomainsJson,
    int MaxProvisionsPerHour);
