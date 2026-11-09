using AqtCoreFW.SharedKernel.Presentation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Api.Controllers;

/// <summary>
/// Read-only view of the deferred-module config flags (<see cref="DeferredModuleFlags"/>) so the
/// FE can hide routes and menu entries for modules that are off in this deployment. The flags are
/// deploy-time configuration, not per-user state — the FE caches them for the session.
///
/// Authentication: required ([Authorize] with no policy), mirroring <see cref="SecurityController"/>:
/// the values are not secrets, but anonymous access would let an unauthenticated caller enumerate
/// which parts of the attack surface are live.
///
/// This controller lives in the HOST (not a module Presentation assembly) on purpose — it must
/// keep answering when the modules it describes are turned off.
/// </summary>
[Route("api/v1/system/module-flags")]
[Authorize]
public sealed class ModuleFlagsController(IConfiguration configuration) : ApiControllerBase
{
    /// <summary>GET /api/v1/system/module-flags — enabled/disabled per deferred module.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ModuleFlagsDto), 200)]
    public IActionResult Get() => Ok(new ModuleFlagsDto(
        Ai: DeferredModuleFlags.IsEnabled(configuration, DeferredModuleFlags.Ai),
        Collaboration: DeferredModuleFlags.IsEnabled(configuration, DeferredModuleFlags.Collaboration),
        BusinessProcess: DeferredModuleFlags.IsEnabled(configuration, DeferredModuleFlags.BusinessProcess),
        Reporting: DeferredModuleFlags.IsEnabled(configuration, DeferredModuleFlags.Reporting),
        Rules: DeferredModuleFlags.IsEnabled(configuration, DeferredModuleFlags.Rules)));
}

/// <summary>Enabled state per deferred module — shape mirrors <see cref="DeferredModuleFlags.ModuleNames"/>.</summary>
public sealed record ModuleFlagsDto(
    bool Ai,
    bool Collaboration,
    bool BusinessProcess,
    bool Reporting,
    bool Rules);
