using AqtCoreFW.Collaboration.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Collaboration.Application.Queries.GetUnreadCount;

public sealed class GetUnreadCountQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetUnreadCountQuery, Result<IReadOnlyList<UnreadCountDto>>>
{
    public async Task<Result<IReadOnlyList<UnreadCountDto>>> Handle(
        GetUnreadCountQuery request,
        CancellationToken cancellationToken)
    {
        // Count messages newer than the member's last-read message per conversation
        // c.PublicId AS ConversationId: expose T2 Guid AK instead of internal long FK
        const string sql = """
            SELECT c.PublicId AS ConversationId,
                   COUNT(m.Id) AS UnreadCount
            FROM collab.ConversationMembers cm
            INNER JOIN collab.Conversations c ON c.Id = cm.ConversationId
                AND c.TenantId = @TenantId AND c.IsDeleted = 0
            LEFT JOIN collab.Messages m ON m.ConversationId = cm.ConversationId
                AND m.IsDeleted = 0
                AND (cm.LastReadMessageId IS NULL
                     OR m.CreatedAt > (
                         SELECT CreatedAt FROM collab.Messages
                         WHERE Id = cm.LastReadMessageId AND IsDeleted = 0))
            WHERE cm.UserId = @UserId AND cm.IsDeleted = 0
            GROUP BY c.PublicId
            """;

        var rows = await db.QueryAsync<UnreadRow>(sql,
            new { request.UserId, request.TenantId }, cancellationToken);

        var result = rows
            .Select(r => new UnreadCountDto(r.ConversationId, r.UnreadCount))
            .ToList();

        return Result.Ok<IReadOnlyList<UnreadCountDto>>(result);
    }

    private sealed class UnreadRow
    {
        public Guid ConversationId { get; init; }  // c.PublicId AS ConversationId
        public int UnreadCount { get; init; }
    }
}
