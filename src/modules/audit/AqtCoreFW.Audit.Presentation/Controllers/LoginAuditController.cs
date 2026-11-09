using AqtCoreFW.Audit.Application.Queries.GetLoginAudit;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Audit.Presentation.Controllers;

/// <summary>Login audit dashboard â€” suspicious authentication activity detection.</summary>
[Route("api/v1/admin/login-audit")]
[Authorize(Policy = PermCodes.Audit.Login.View)]
[RequiresMfa]
public sealed class LoginAuditController(ISender mediator, ITenantContext tenantContext) : ApiControllerBase
{
    /// <summary>Query login attempts with filters.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? userId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] bool? success,
        [FromQuery][StringLength(200)] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        // Non-SystemAdmin callers are bound to their own tenant; SystemAdmin sees all tenants.
        return ToApiResponse(await mediator.Send(
            new GetLoginAuditQuery(
                userId, from, to, success,
                tenantContext.TenantId, tenantContext.IsSystemAdmin,
                search, page, pageSize),
            cancellationToken));
    }
}
