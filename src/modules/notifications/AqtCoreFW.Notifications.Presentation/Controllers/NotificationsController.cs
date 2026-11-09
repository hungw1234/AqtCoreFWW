using AqtCoreFW.SharedKernel.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using AqtCoreFW.SharedKernel.Presentation;
using AqtCoreFW.Notifications.Application.Commands.MarkAllNotificationsRead;
using AqtCoreFW.Notifications.Application.Commands.MarkNotificationRead;
using AqtCoreFW.Notifications.Application.Commands.SendNotification;
using AqtCoreFW.Notifications.Application.Queries.GetUserNotifications;
using AqtCoreFW.Notifications.Application.Queries.GetUnreadNotificationCount;

namespace AqtCoreFW.Notifications.Presentation.Controllers;

/// <summary>
/// Notifications REST API â€” user-facing endpoints.
/// GET list, unread count, mark read. All require authenticated user.
/// </summary>
[Authorize]
[Route("api/v1/notifications")]
public sealed class NotificationsController(ISender mediator) : ApiControllerBase
{
    /// <summary>List paginated notifications for the current user.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetMyNotifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? channel = null,
        [FromQuery] bool? isRead = null,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        var query = new GetUserNotificationsQuery(userId, tenantId, page, pageSize, channel, isRead);
        return ToApiResponse(await mediator.Send(query, cancellationToken));
    }

    /// <summary>Unread notification count for badge display.</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(int), 200)]
    public async Task<IActionResult> GetUnreadCount(
        CancellationToken cancellationToken = default)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        return ToApiResponse(await mediator.Send(
            new GetUnreadNotificationCountQuery(userId, tenantId), cancellationToken));
    }

    /// <summary>Mark a single notification as read.</summary>
    [HttpPatch("{id:guid}/read")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> MarkRead(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        return ToApiResponse(await mediator.Send(
            new MarkNotificationReadCommand(id, tenantId, userId), cancellationToken));
    }

    /// <summary>Mark all notifications as read for the current user.</summary>
    [HttpPost("read-all")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> MarkAllRead(
        CancellationToken cancellationToken = default)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        return ToApiResponse(await mediator.Send(
            new MarkAllNotificationsReadCommand(userId, tenantId), cancellationToken));
    }

    /// <summary>Send a notification (internal/programmatic â€” e.g. from admin tools).</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Notif.Msg.Send)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(typeof(Guid), 200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Send(
        [FromBody] SendNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        // The tenant comes from the caller's own claims, never from the body: the handler resolves
        // the recipient's real mailbox from this tenant, so trusting a client-supplied value would
        // let any sender address a user in another tenant. SystemAdmin still crosses tenants the
        // same way as every other action here — via the X-Tenant-Id header.
        var tenantId = ResolveTenantId();
        return ToApiResponse(await mediator.Send(command with { TenantId = tenantId }, cancellationToken));
    }
}
