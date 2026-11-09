using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using AqtCoreFW.SystemParams.Application.Commands;
using AqtCoreFW.SystemParams.Application.Queries;
using AqtCoreFW.SystemParams.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.SystemParams.Presentation.Controllers;

/// <summary>
/// Admin: full CRUD for system announcements.
/// Public: GET /api/v1/announcements/active â€” cached 1min.
/// </summary>
[ApiController]
public class AnnouncementsController(ISender mediator) : ApiControllerBase
{
    [HttpGet("api/v1/admin/announcements")]
    [Authorize(Policy = PermCodes.SysParam.Announce.Manage)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await mediator.Send(new GetAllAnnouncementsQuery(), ct);
        return ToApiResponse(result);
    }

    [HttpPost("api/v1/admin/announcements")]
    [Authorize(Policy = PermCodes.SysParam.Announce.Manage)]
    [EnableRateLimiting("write-ops")]
    public async Task<IActionResult> Create([FromBody] CreateAnnouncementRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateAnnouncementCommand(
            req.Title, req.Content, req.Severity,
            req.StartAt, req.EndAt, req.TargetRoles), ct);
        return ToApiResponse(result);
    }

    [HttpPut("api/v1/admin/announcements/{id:long}")]
    [Authorize(Policy = PermCodes.SysParam.Announce.Manage)]
    [EnableRateLimiting("write-ops")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateAnnouncementRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateAnnouncementCommand(
            id, req.Title, req.Content, req.Severity,
            req.StartAt, req.EndAt, req.IsActive), ct);
        return ToApiResponse(result);
    }

    [HttpDelete("api/v1/admin/announcements/{id:long}")]
    [Authorize(Policy = PermCodes.SysParam.Announce.Manage)]
    [EnableRateLimiting("write-ops")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteAnnouncementCommand(id), ct);
        return ToApiResponse(result);
    }

    [HttpGet("api/v1/announcements/active")]
    [AllowAnonymous]
    public async Task<IActionResult> GetActive(CancellationToken ct)
    {
        var result = await mediator.Send(new GetActiveAnnouncementsQuery(), ct);
        return ToApiResponse(result);
    }
}

// --- Request DTOs ---

public record CreateAnnouncementRequest(
    string Title, string Content, AnnouncementSeverity Severity,
    DateTimeOffset? StartAt, DateTimeOffset? EndAt, string? TargetRoles);

public record UpdateAnnouncementRequest(
    string Title, string Content, AnnouncementSeverity Severity,
    DateTimeOffset? StartAt, DateTimeOffset? EndAt, bool IsActive);
