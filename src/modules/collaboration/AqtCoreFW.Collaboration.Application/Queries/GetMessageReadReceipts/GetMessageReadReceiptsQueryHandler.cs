using AqtCoreFW.Collaboration.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Collaboration.Application.Queries.GetMessageReadReceipts;

public sealed class GetMessageReadReceiptsQueryHandler(
    IReadDbConnection db,
    IIdentityModuleClient identityClient)
    : IRequestHandler<GetMessageReadReceiptsQuery, Result<IReadOnlyList<MessageReadReceiptDto>>>
{
    public async Task<Result<IReadOnlyList<MessageReadReceiptDto>>> Handle(
        GetMessageReadReceiptsQuery request,
        CancellationToken cancellationToken)
    {
        // Resolve the message (by PublicId, tenant-scoped) AND the caller's membership in one round-trip.
        // LEFT JOIN on the caller's membership: no row at all => message not found / not in tenant;
        // row with IsMember=0 => caller is not a participant => 403.
        const string lookupSql = """
            SELECT m.Id AS MessageInternalId,
                   CASE WHEN cm.UserId IS NOT NULL THEN 1 ELSE 0 END AS IsMember
            FROM collab.Messages m
            INNER JOIN collab.Conversations c ON c.Id = m.ConversationId
            LEFT JOIN collab.ConversationMembers cm
                ON cm.ConversationId = c.Id AND cm.UserId = @RequesterId AND cm.IsDeleted = 0
            WHERE m.PublicId = @MessageId AND m.TenantId = @TenantId AND m.IsDeleted = 0
            """;

        var lookup = await db.QueryFirstOrDefaultAsync<MessageLookupRow>(
            lookupSql, new { request.MessageId, request.TenantId, request.RequesterId }, cancellationToken);

        if (lookup is null)
            return Result.Fail(new NotFoundError($"Message {request.MessageId} not found."));

        if (lookup.IsMember == 0)
            return Result.Fail(new ForbiddenError(
                $"User {request.RequesterId} is not a participant of the conversation for message {request.MessageId}."));

        const string readersSql = """
            SELECT rs.UserId, rs.ReadAt
            FROM collab.MessageReadStates rs
            WHERE rs.MessageId = @MessageInternalId AND rs.IsDeleted = 0
            ORDER BY rs.ReadAt
            """;

        var readers = (await db.QueryAsync<ReaderRow>(
            readersSql, new { lookup.MessageInternalId }, cancellationToken)).ToList();

        if (readers.Count == 0)
            return Result.Ok<IReadOnlyList<MessageReadReceiptDto>>([]);

        // Resolve reader display names via the Identity module (same pattern as message queries).
        var userMap = await identityClient.GetUserInfoByIdsAsync(
            readers.Select(r => r.UserId).Distinct(), request.TenantId, cancellationToken);

        var result = readers
            .Select(r => new MessageReadReceiptDto(
                r.UserId,
                userMap.TryGetValue(r.UserId, out var u) ? u.FullName : null,
                r.ReadAt))
            .ToList();

        return Result.Ok<IReadOnlyList<MessageReadReceiptDto>>(result);
    }

    private sealed class MessageLookupRow
    {
        public long MessageInternalId { get; init; }
        public int IsMember { get; init; }
    }

    private sealed class ReaderRow
    {
        public Guid UserId { get; init; }
        public DateTime ReadAt { get; init; }
    }
}
