using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using AqtCoreFW.SystemParams.Application.Commands;
using AqtCoreFW.SystemParams.Application.Services;
using AqtCoreFW.SystemParams.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.SystemParams.Presentation.Controllers;

/// <summary>Admin CRUD for system parameters. Requires Admin role.</summary>
[ApiController]
[Route("api/v1/admin/system-params")]
[Authorize(Policy = PermCodes.SysParam.Param.Manage)]
[RequiresMfa]
public class SystemParamsController(ISender mediator) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await mediator.Send(new GetAllSystemParamsQuery(ResolveTenantId()), ct);
        return ToApiResponse(result);
    }

    [HttpGet("{key}")]
    public async Task<IActionResult> GetOne(string key, CancellationToken ct)
    {
        var result = await mediator.Send(new GetSystemParamByKeyQuery(key, ResolveTenantId()), ct);
        return ToApiResponse(result);
    }

    [HttpPut("{key}")]
    [EnableRateLimiting("write-ops")]
    public async Task<IActionResult> Update(
        string key, [FromBody] UpdateSystemParamRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new UpdateSystemParamCommand(key, req.Value, ResolveTenantId()), ct);
        return ToApiResponse(result);
    }
}

public record UpdateSystemParamRequest(string Value);

/// <summary>
/// Public session config endpoint â€” any authenticated user can read idle timeout settings.
/// Separate from admin controller to avoid exposing all system params.
/// </summary>
[ApiController]
[Route("api/v1/session-config")]
[Authorize]
public class SessionConfigController(ISystemParamService paramService) : ApiControllerBase
{
    /// <summary>GET /api/v1/session-config â€” returns idle timeout + countdown for FE idle detection.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var timeoutStr = await paramService.GetAsync("session_timeout_minutes", ct: ct);
        var countdownStr = await paramService.GetAsync("session_countdown_seconds", ct: ct);
        var timeout = int.TryParse(timeoutStr, out var t) ? t : 30;
        var countdown = int.TryParse(countdownStr, out var c) ? c : 60;

        return Ok(SharedKernel.Api.ApiResponse<object>.Ok(new
        {
            idleTimeoutMinutes = Math.Clamp(timeout, 1, 480),
            countdownSeconds = Math.Clamp(countdown, 10, 300),
        }));
    }
}
