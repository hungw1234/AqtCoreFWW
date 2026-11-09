using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Admin CRUD for per-tenant digital signature provider configuration (QĐ 742 §5.1).
/// Singleton config per tenant — GET returns current config; PUT creates or updates.
/// SystemAdmin-only (SECURITY.SIGNATURE_CONFIG.* excluded from tenant-Admin role).
///
/// [RequiresMfa]: all writes require a valid MFA session (QĐ 742 1.6.b).
/// </summary>
[Route("api/v1/admin/security/signature-config")]
[Authorize(Policy = PermCodes.Security.SignatureConfig.View)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class SignatureConfigAdminController(ISender mediator) : ApiControllerBase
{
    /// <summary>
    /// GET /api/v1/admin/security/signature-config/{tenantId}
    /// Returns the current signature provider config for the given tenant.
    /// Returns 404 if the tenant has not yet configured a provider.
    /// </summary>
    [HttpGet("{tenantId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<TenantSignatureConfigDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(new GetTenantSignatureConfigQuery(tenantId), ct));

    /// <summary>
    /// PUT /api/v1/admin/security/signature-config/{tenantId}
    /// Creates or updates the signature provider config for the given tenant (upsert).
    /// Returns 204 No Content on success.
    /// </summary>
    [HttpPut("{tenantId:guid}")]
    [Authorize(Policy = PermCodes.Security.SignatureConfig.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 409)]
    public async Task<IActionResult> Upsert(
        Guid tenantId,
        [FromBody] UpsertSignatureConfigRequest request,
        CancellationToken ct = default)
    {
        var actorId = ResolveUserId();

        // RowVersion decoded from base64 (as serialised in TenantSignatureConfigDto.RowVersion).
        // Null on first-ever creation (no existing config to conflict with).
        byte[]? rowVersion = null;
        if (request.RowVersion is not null)
        {
            try { rowVersion = Convert.FromBase64String(request.RowVersion); }
            catch (FormatException)
            {
                return BadRequest(new { error = "RowVersion must be a valid base64-encoded string." });
            }
        }

        return ToApiResponse(await mediator.Send(
            new UpdateTenantSignatureConfigCommand(
                tenantId,
                request.ProviderKey,
                request.ApiCredentialsRef,
                request.AllowedCaRootThumbprints ?? string.Empty,
                request.OcspMode,
                request.IsEnabled,
                request.SignFlowMode,
                actorId,
                rowVersion), ct));
    }

    /// <summary>
    /// GET /api/v1/admin/security/signature-config/available-providers
    /// Returns all IDigitalSignatureProvider implementations registered in DI.
    /// Used by admin UI to populate the provider selection dropdown.
    /// </summary>
    [HttpGet("available-providers")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AvailableProviderDto>>), 200)]
    public async Task<IActionResult> GetAvailableProviders(CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(new GetAvailableProvidersQuery(), ct));
}

// ── Request DTOs ────────────────────────────────────────────────────────────

/// <summary>
/// PUT body for /admin/security/signature-config/{tenantId}.
/// RowVersion: base64-encoded EF concurrency token from GET response.
/// Omit (null) on first creation; supply on every subsequent update to detect stale-tab races (→ 409).
/// </summary>
public sealed record UpsertSignatureConfigRequest(
    string ProviderKey,
    string? ApiCredentialsRef,
    string? AllowedCaRootThumbprints,
    OcspMode OcspMode,
    bool IsEnabled,
    SignatureFlowMode SignFlowMode = SignatureFlowMode.Async,
    string? RowVersion = null);
