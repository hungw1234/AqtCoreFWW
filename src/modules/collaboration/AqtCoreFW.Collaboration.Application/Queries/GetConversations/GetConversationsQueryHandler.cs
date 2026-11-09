using AqtCoreFW.Collaboration.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Collaboration.Application.Queries.GetConversations;

public sealed class GetConversationsQueryHandler(
    IReadDbConnection db,
    IIdentityModuleClient identityClient)
    : IRequestHandler<GetConversationsQuery, Result<IReadOnlyList<ConversationDto>>>
{
    public async Task<Result<IReadOnlyList<ConversationDto>>> Handle(
        GetConversationsQuery request,
        CancellationToken cancellationToken)
    {
        // Single query: conversations + last message + unread count (same schema, no cross-join)
        // c.PublicId AS Id: T2 — expose Guid PublicId as 'Id' for FE; c.Id (long) used only in JOINs
        const string sql = """
            SELECT c.PublicId AS Id, c.Type, c.Name, c.CreatedBy, c.LastMessageAt, c.IsArchived,
                   (SELECT COUNT(*) FROM collab.ConversationMembers
                    WHERE ConversationId = c.Id AND IsDeleted = 0) AS MemberCount,
                   lm.Content AS LastMessagePreview,
                   lm.SenderId AS LastMessageSenderId,
                   (SELECT COUNT(*) FROM collab.Messages m2
                    WHERE m2.ConversationId = c.Id AND m2.IsDeleted = 0
                      AND m2.CreatedAt > COALESCE(
                          (SELECT CreatedAt FROM collab.Messages
                           WHERE Id = cm.LastReadMessageId AND IsDeleted = 0),
                          '1900-01-01')
                   ) AS UnreadCount
            FROM collab.Conversations c
            INNER JOIN collab.ConversationMembers cm ON cm.ConversationId = c.Id
                AND cm.UserId = @UserId AND cm.IsDeleted = 0
            OUTER APPLY (
                SELECT TOP 1 Content, SenderId FROM collab.Messages
                WHERE ConversationId = c.Id AND IsDeleted = 0
                ORDER BY CreatedAt DESC
            ) lm
            WHERE c.TenantId = @TenantId AND c.IsDeleted = 0
            ORDER BY c.LastMessageAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var rows = await db.QueryAsync<ConversationRow>(sql, new
        {
            request.UserId,
            request.TenantId,
            Offset = (page - 1) * pageSize,
            PageSize = pageSize
        }, cancellationToken);

        var rowList = rows.ToList();
        if (rowList.Count == 0)
            return Result.Ok<IReadOnlyList<ConversationDto>>([]);

        // Collect user IDs: last-message senders + other members for Direct convs
        var userIdsToResolve = new HashSet<Guid>();
        foreach (var r in rowList)
        {
            if (r.LastMessageSenderId.HasValue)
                userIdsToResolve.Add(r.LastMessageSenderId.Value);
        }

        // For Direct conversations, find other member IDs
        // r.Id = Guid PublicId (T2 AK); ConversationMembers.ConversationId is long FK → JOIN via c.PublicId
        var directConvPublicIds = rowList
            .Where(r => r.Type == "Direct")
            .Select(r => r.Id)
            .ToList();

        // directOtherMember: keyed by Conversation PublicId (Guid) → other user's Id (Guid)
        Dictionary<Guid, Guid> directOtherMember = [];
        if (directConvPublicIds.Count > 0)
        {
            const string memberSql = """
                SELECT c.PublicId AS ConversationPublicId, cm.UserId
                FROM collab.ConversationMembers cm
                INNER JOIN collab.Conversations c ON c.Id = cm.ConversationId
                WHERE c.PublicId IN @ConvPublicIds AND cm.UserId != @UserId AND cm.IsDeleted = 0
                """;
            var members = await db.QueryAsync<MemberRow>(memberSql,
                new { ConvPublicIds = directConvPublicIds, request.UserId }, cancellationToken);
            foreach (var m in members)
            {
                directOtherMember[m.ConversationPublicId] = m.UserId;
                userIdsToResolve.Add(m.UserId);
            }
        }

        // Bulk resolve user names via cross-module client (no cross-schema SQL join)
        var userMap = userIdsToResolve.Count > 0
            ? await identityClient.GetUserInfoByIdsAsync(userIdsToResolve, request.TenantId, cancellationToken)
            : (IReadOnlyDictionary<Guid, UserInfoDto>)new Dictionary<Guid, UserInfoDto>();

        var result = rowList.Select(r =>
        {
            var senderName = r.LastMessageSenderId.HasValue
                && userMap.TryGetValue(r.LastMessageSenderId.Value, out var sender)
                ? sender.FullName : null;

            // For Direct convs, participant = other user's name — r.Id = Guid PublicId (T2 AK)
            List<string> participantNames = [];
            if (r.Type == "Direct" && directOtherMember.TryGetValue(r.Id, out var otherId)
                && userMap.TryGetValue(otherId, out var other))
            {
                participantNames.Add(other.FullName ?? "");
            }

            return new ConversationDto(
                r.Id, r.Type, r.Name, r.CreatedBy,
                r.LastMessageAt, r.IsArchived, r.MemberCount,
                r.LastMessagePreview?.Length > 100
                    ? r.LastMessagePreview[..100] + "…"
                    : r.LastMessagePreview,
                senderName,
                r.UnreadCount,
                participantNames);
        }).ToList();

        return Result.Ok<IReadOnlyList<ConversationDto>>(result);
    }

    // Class-based for Dapper materialization (nullable params safe)
    private sealed class ConversationRow
    {
        public Guid Id { get; init; }
        public string Type { get; init; } = "";
        public string? Name { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime? LastMessageAt { get; init; }
        public bool IsArchived { get; init; }
        public int MemberCount { get; init; }
        public string? LastMessagePreview { get; init; }
        public Guid? LastMessageSenderId { get; init; }
        public int UnreadCount { get; init; }
    }

    private sealed class MemberRow
    {
        public Guid ConversationPublicId { get; init; }
        public Guid UserId { get; init; }
    }
}
