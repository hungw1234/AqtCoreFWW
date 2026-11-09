using AqtCoreFW.Collaboration.Application.Commands.DeleteMessage;
using AqtCoreFW.Collaboration.Application.Commands.MarkAsRead;
using AqtCoreFW.Collaboration.Application.Commands.SendMessage;
using AqtCoreFW.Collaboration.Application.Queries.GetMessageReadReceipts;
using AqtCoreFW.Collaboration.Application.Queries.GetUnreadCount;
using AqtCoreFW.Collaboration.Application.Queries.SearchMessages;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Collaboration.Presentation.Controllers;

/// <summary>
/// Messages REST API — send, delete, mark-as-read, search, and unread counts.
/// TenantId resolved from JWT claim via ITenantContext.
/// </summary>
[Route("api/v1/messages")]
[Authorize]
public sealed class MessagesController(ISender mediator, ITenantContext tenantContext, ICurrentUser currentUser)
    : ApiControllerBase
{
    /// <summary>Send a message to a conversation.</summary>
    [HttpPost]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Send(
        [FromBody] SendMessageBody body,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? Guid.Empty;
        return ToApiResponse(await mediator.Send(
            new SendMessageCommand(
                body.ConversationId,
                currentUser.UserId,
                body.Content,
                body.ContentType,
                tenantId,
                body.ReplyToId),
            cancellationToken));
    }

    /// <summary>Soft-delete a message.</summary>
    [HttpDelete("{id:guid}")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        ToApiResponse(await mediator.Send(new DeleteMessageCommand(id, currentUser.UserId), cancellationToken));

    /// <summary>Mark a message as read by the current user.</summary>
    [HttpPost("{id:guid}/read")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> MarkRead(
        Guid id,
        [FromBody] MarkReadBody body,
        CancellationToken cancellationToken) =>
        ToApiResponse(await mediator.Send(
            new MarkAsReadCommand(body.ConversationId, id, currentUser.UserId),
            cancellationToken));

    /// <summary>Get read receipts ("seen by") for a message — visible to conversation participants only.</summary>
    [HttpGet("{id:guid}/read-receipts")]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ReadReceipts(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? Guid.Empty;
        return ToApiResponse(await mediator.Send(
            new GetMessageReadReceiptsQuery(id, tenantId, currentUser.UserId),
            cancellationToken));
    }

    /// <summary>Full-text search within a conversation.</summary>
    [HttpGet("search")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> Search(
        [FromQuery] Guid conversationId,
        [FromQuery] string q,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new SearchMessagesQuery(conversationId, q, pageSize),
            cancellationToken));

    /// <summary>Get unread message counts per conversation for the current user.</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> UnreadCount(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? Guid.Empty;
        return ToApiResponse(await mediator.Send(
            new GetUnreadCountQuery(currentUser.UserId, tenantId),
            cancellationToken));
    }
}

// Request body records
public sealed record SendMessageBody(
    Guid ConversationId,
    string Content,
    ContentType ContentType,
    Guid? ReplyToId);

public sealed record MarkReadBody(Guid ConversationId);
