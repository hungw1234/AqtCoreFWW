using AqtCoreFW.Collaboration.Application.Commands.AddMember;
using AqtCoreFW.Collaboration.Application.Commands.ArchiveConversation;
using AqtCoreFW.Collaboration.Application.Commands.CreateConversation;
using AqtCoreFW.Collaboration.Application.Queries.GetConversations;
using AqtCoreFW.Collaboration.Application.Queries.GetMessages;
using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Collaboration.Presentation.Controllers;

/// <summary>
/// Conversations REST API — create, list, archive conversations; manage members; fetch messages.
/// TenantId resolved from JWT claim via ITenantContext.
/// </summary>
[Route("api/v1/conversations")]
[Authorize]
public sealed class ConversationsController(ISender mediator, ITenantContext tenantContext, ICurrentUser currentUser)
    : ApiControllerBase
{
    /// <summary>Create a new conversation.</summary>
    [HttpPost]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateConversationBody body,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? Guid.Empty;
        return ToApiResponse(await mediator.Send(
            new CreateConversationCommand(
                body.Type,
                body.Name,
                body.MemberUserIds,
                currentUser.UserId,
                tenantId),
            cancellationToken));
    }

    /// <summary>List conversations for the current user.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetConversations(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? Guid.Empty;
        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(
            new GetConversationsQuery(currentUser.UserId, tenantId, page, pageSize),
            cancellationToken));
    }

    /// <summary>Get paginated messages for a conversation.</summary>
    [HttpGet("{id:guid}/messages")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetMessages(
        Guid id,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? before = null,
        [FromQuery] Guid? after = null,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetMessagesQuery(id, pageSize, before, after),
            cancellationToken));

    /// <summary>Archive a conversation.</summary>
    [HttpPatch("{id:guid}/archive")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken) =>
        ToApiResponse(await mediator.Send(
            new ArchiveConversationCommand(id, currentUser.UserId), cancellationToken));

    /// <summary>Add a member to a conversation.</summary>
    [HttpPost("{id:guid}/members")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> AddMember(
        Guid id,
        [FromBody] AddMemberBody body,
        CancellationToken cancellationToken) =>
        ToApiResponse(await mediator.Send(
            new AddMemberCommand(id, body.UserId, body.Role, currentUser.UserId),
            cancellationToken));
}

// Request body records — lightweight, no entity types (GOV_SEC_004 mass-assignment prevention)
public sealed record CreateConversationBody(
    ConversationType Type,
    string? Name,
    IReadOnlyList<Guid> MemberUserIds);

public sealed record AddMemberBody(Guid UserId, MemberRole Role);
