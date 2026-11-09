using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using AqtCoreFW.SystemParams.Application.Queries;
using AqtCoreFW.SystemParams.Application.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.SystemParams.Presentation.Controllers;

/// <summary>
/// Admin: manage feature flags (SystemParameter with key prefix "feature:").
/// Public: GET /api/v1/feature-flags/{key} â€” sync IsEnabled check from L0 ConcurrentDict.
/// </summary>
[ApiController]
public class FeatureFlagsController(
    ISender mediator,
    ISystemParamService paramService,
    IFeatureFlagService featureFlagService,
    ITenantContext tenantContext) : ApiControllerBase
{
    [HttpGet("api/v1/admin/feature-flags")]
    [Authorize(Policy = PermCodes.SysParam.Flag.View)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await mediator.Send(new GetAllFeatureFlagsQuery(), ct);
        return ToApiResponse(result);
    }

    /// <summary>
    /// Flags that are deployment-wide security posture, not per-tenant capability. These are written to
    /// the GLOBAL row and require SystemAdmin; a tenant must not be able to opt itself out.
    ///
    /// Writing them per-tenant would not work anyway: FeatureFlagService's cache is filled by
    /// ReloadAllAsync running in a background service with no tenant, so under the production
    /// least-privilege login the RLS filter admits only NULL-tenant rows — a tenant-scoped row would be
    /// written, invisible to every reader, and the toggle would silently do nothing.
    /// </summary>
    private static readonly HashSet<string> GlobalOnlyFlags = new(StringComparer.Ordinal)
    {
        "feature:files.virus-scan",
        // Untrusted-PDF upload-sign gate — deployment-wide posture (a tenant must not sign uploaded PDFs
        // when the deployment defers that path), so it toggles on the global row only.
        "feature:signing.upload-arbitrary-pdf"
    };

    [HttpPut("api/v1/admin/feature-flags/{flagName}")]
    [Authorize(Policy = PermCodes.SysParam.Flag.Manage)]
    [EnableRateLimiting("write-ops")]
    public async Task<IActionResult> Toggle(
        string flagName, [FromBody] ToggleFeatureFlagRequest req,
        CancellationToken ct)
    {
        var key = $"feature:{flagName.ToLowerInvariant()}";

        if (GlobalOnlyFlags.Contains(key))
        {
            if (!tenantContext.IsSystemAdmin)
                return Forbid();

            // Global row: tenantId omitted, not null-coalesced — SetAsync(key, value, ct) is the
            // NULL-tenant overload.
            await paramService.SetAsync(key, req.IsEnabled, ct);
            return Ok(ApiResponse<object>.Ok(new { key, isEnabled = req.IsEnabled, scope = "global" }));
        }

        var tenantId = ResolveTenantId().ToString();
        await paramService.SetAsync(key, req.IsEnabled, tenantId, ct);
        return Ok(ApiResponse<object>.Ok(new { key, isEnabled = req.IsEnabled }));
    }

    [HttpGet("api/v1/feature-flags/{key}")]
    [AllowAnonymous]
    public IActionResult IsEnabled(string key, [FromQuery] string? tenantId)
    {
        var flagKey = key.StartsWith("feature:", StringComparison.OrdinalIgnoreCase)
            ? key : $"feature:{key}";
        var enabled = featureFlagService.IsEnabled(flagKey, tenantId);
        return Ok(ApiResponse<object>.Ok(new { key = flagKey, enabled }));
    }
}

public record ToggleFeatureFlagRequest(bool IsEnabled);
