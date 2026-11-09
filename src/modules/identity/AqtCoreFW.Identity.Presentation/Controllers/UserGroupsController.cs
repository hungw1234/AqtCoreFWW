using AqtCoreFW.Identity.Application.Queries.ListActiveUserGroups;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Picker endpoint for user groups — used by Workflow assignment editor ByGroup multiselect.
/// Requires WORKFLOW.DEF.MANAGE (Workflow definition admin; no MFA — read-only picker).
/// Distinct from /api/v1/admin/groups (GroupsAdminController) which requires Identity.Group.View + MFA.
/// </summary>
[Route("api/v1/user-groups")]
[Authorize(Policy = PermCodes.Workflow.Def.Manage)]
[EnableRateLimiting("read-ops")]
public sealed class UserGroupsController(ISender mediator) : ApiControllerBase
{
    /// <summary>
    /// List user groups for the caller's tenant.
    /// Query param: <c>activeOnly=true</c> (default) filters to active groups only.
    /// Returns lightweight summary (PublicId, Code, Name, IsActive) sorted by Name ASC.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] bool activeOnly = true,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var result = await mediator.Send(new ListActiveUserGroupsQuery(tenantId, activeOnly), ct);
        return ToApiResponse(result);
    }
}
