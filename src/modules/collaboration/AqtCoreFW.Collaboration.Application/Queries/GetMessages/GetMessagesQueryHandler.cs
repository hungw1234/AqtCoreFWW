using AqtCoreFW.Collaboration.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Collaboration.Application.Queries.GetMessages;

public sealed class GetMessagesQueryHandler(
    IReadDbConnection db,
    ITenantContext tenantContext,
    IIdentityModuleClient identityClient)
    : IRequestHandler<GetMessagesQuery, Result<IReadOnlyList<MessageDto>>>
{
    public async Task<Result<IReadOnlyList<MessageDto>>> Handle(
        GetMessagesQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Missing tenant context for message retrieval.");

        // Cursor-based: before = older, after = newer; default newest first
        // T2 projections: PublicId AS Id, c.PublicId AS ConversationId for FE-stable Guid DTOs
        // ConversationId param = Guid PublicId (FE-facing); JOIN to get internal long for WHERE
        // Cursor params (BeforeMessageId/AfterMessageId) = Guid PublicId — match via m.PublicId
        const string beforeSql = """
            SELECT m.PublicId AS Id,
                   c.PublicId AS ConversationId,
                   m.SenderId, m.Content, m.ContentType,
                   rp.PublicId AS ReplyToId,
                   m.CreatedAt, m.EditedAt, m.DeletedAt, m.IsSystemMessage
            FROM collab.Messages m
            INNER JOIN collab.Conversations c ON c.Id = m.ConversationId
            LEFT JOIN collab.Messages rp ON rp.Id = m.ReplyToId
            WHERE m.TenantId = @TenantId AND c.PublicId = @ConversationId AND m.IsDeleted = 0
              AND m.CreatedAt < (SELECT CreatedAt FROM collab.Messages mc WHERE mc.PublicId = @CursorId AND mc.TenantId = @TenantId AND mc.IsDeleted = 0)
            ORDER BY m.CreatedAt DESC
            OFFSET 0 ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        const string afterSql = """
            SELECT m.PublicId AS Id,
                   c.PublicId AS ConversationId,
                   m.SenderId, m.Content, m.ContentType,
                   rp.PublicId AS ReplyToId,
                   m.CreatedAt, m.EditedAt, m.DeletedAt, m.IsSystemMessage
            FROM collab.Messages m
            INNER JOIN collab.Conversations c ON c.Id = m.ConversationId
            LEFT JOIN collab.Messages rp ON rp.Id = m.ReplyToId
            WHERE m.TenantId = @TenantId AND c.PublicId = @ConversationId AND m.IsDeleted = 0
              AND m.CreatedAt > (SELECT CreatedAt FROM collab.Messages mc WHERE mc.PublicId = @CursorId AND mc.TenantId = @TenantId AND mc.IsDeleted = 0)
            ORDER BY m.CreatedAt ASC
            OFFSET 0 ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        const string defaultSql = """
            SELECT m.PublicId AS Id,
                   c.PublicId AS ConversationId,
                   m.SenderId, m.Content, m.ContentType,
                   rp.PublicId AS ReplyToId,
                   m.CreatedAt, m.EditedAt, m.DeletedAt, m.IsSystemMessage
            FROM collab.Messages m
            INNER JOIN collab.Conversations c ON c.Id = m.ConversationId
            LEFT JOIN collab.Messages rp ON rp.Id = m.ReplyToId
            WHERE m.TenantId = @TenantId AND c.PublicId = @ConversationId AND m.IsDeleted = 0
            ORDER BY m.CreatedAt DESC
            OFFSET 0 ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        // Cursor-based pagination — Math.Clamp on PageSize only (no Page param). AdminMax=100 ceiling.
        var pageSize = Math.Clamp(request.PageSize, 1, PaginationDefaults.AdminMax);

        string sql;
        object param;

        if (request.BeforeMessageId.HasValue)
        {
            sql = beforeSql;
            param = new { TenantId = tenantId, request.ConversationId, CursorId = request.BeforeMessageId.Value, PageSize = pageSize };
        }
        else if (request.AfterMessageId.HasValue)
        {
            sql = afterSql;
            param = new { TenantId = tenantId, request.ConversationId, CursorId = request.AfterMessageId.Value, PageSize = pageSize };
        }
        else
        {
            sql = defaultSql;
            param = new { TenantId = tenantId, request.ConversationId, PageSize = pageSize };
        }

        var rows = await db.QueryAsync<MessageRow>(sql, param, cancellationToken);
        var rowList = rows.ToList();

        // Resolve sender names via cross-module client
        var senderIds = rowList.Select(r => r.SenderId).Distinct();
        var userMap = await identityClient.GetUserInfoByIdsAsync(senderIds, tenantId, cancellationToken);

        var result = rowList
            .Select(r => new MessageDto(
                r.Id, r.ConversationId, r.SenderId,
                userMap.TryGetValue(r.SenderId, out var u) ? u.FullName : null,
                r.Content, r.ContentType,
                r.ReplyToId, r.CreatedAt, r.EditedAt,
                r.DeletedAt.HasValue, r.IsSystemMessage))
            .ToList();

        return Result.Ok<IReadOnlyList<MessageDto>>(result);
    }

    // Class-based for Dapper materialization: Guid fields map from PublicId AS alias
    private sealed class MessageRow
    {
        public Guid Id { get; init; }           // PublicId AS Id
        public Guid ConversationId { get; init; } // c.PublicId AS ConversationId
        public Guid SenderId { get; init; }
        public string Content { get; init; } = "";
        public string ContentType { get; init; } = "";
        public Guid? ReplyToId { get; init; }   // rp.PublicId AS ReplyToId (nullable)
        public DateTimeOffset CreatedAt { get; init; }
        public DateTime? EditedAt { get; init; }
        public DateTime? DeletedAt { get; init; }
        public bool IsSystemMessage { get; init; }
    }
}
