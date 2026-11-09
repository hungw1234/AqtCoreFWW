using AqtCoreFW.Collaboration.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Collaboration.Application.Queries.SearchMessages;

public sealed class SearchMessagesQueryHandler(
    IReadDbConnection db,
    ITenantContext tenantContext,
    IIdentityModuleClient identityClient)
    : IRequestHandler<SearchMessagesQuery, Result<IReadOnlyList<MessageDto>>>
{
    public async Task<Result<IReadOnlyList<MessageDto>>> Handle(
        SearchMessagesQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Missing tenant context for message search.");

        // T2 projections: PublicId AS Id, c.PublicId AS ConversationId for FE-stable Guid DTOs
        // request.ConversationId = Guid PublicId (FE-facing); match via c.PublicId in JOIN
        const string sql = """
            SELECT m.PublicId AS Id,
                   c.PublicId AS ConversationId,
                   m.SenderId, m.Content, m.ContentType,
                   rp.PublicId AS ReplyToId,
                   m.CreatedAt, m.EditedAt, m.DeletedAt, m.IsSystemMessage
            FROM collab.Messages m
            INNER JOIN collab.Conversations c ON c.Id = m.ConversationId
            LEFT JOIN collab.Messages rp ON rp.Id = m.ReplyToId
            WHERE m.TenantId = @TenantId
              AND c.PublicId = @ConversationId
              AND m.IsDeleted = 0
              AND m.Content LIKE @SearchPattern ESCAPE '\'
            ORDER BY m.CreatedAt DESC
            OFFSET 0 ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        // Cursor-less search — Math.Clamp on PageSize only (no Page param). AdminMax=100 ceiling.
        var pageSize = Math.Clamp(request.PageSize, 1, PaginationDefaults.AdminMax);

        // Escape SQL LIKE wildcards to prevent injection via search term
        var escapedTerm = request.SearchTerm.EscapeSqlLike();
        var rows = await db.QueryAsync<MessageRow>(sql, new
        {
            TenantId = tenantId,
            request.ConversationId,
            SearchPattern = $"%{escapedTerm}%",
            PageSize = pageSize
        }, cancellationToken);

        // Resolve sender names via cross-module client
        var senderIds = rows.Select(r => r.SenderId).Distinct();
        var userMap = await identityClient.GetUserInfoByIdsAsync(senderIds, tenantId, cancellationToken);

        var result = rows
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
        public Guid Id { get; init; }             // m.PublicId AS Id
        public Guid ConversationId { get; init; } // c.PublicId AS ConversationId
        public Guid SenderId { get; init; }
        public string Content { get; init; } = "";
        public string ContentType { get; init; } = "";
        public Guid? ReplyToId { get; init; }     // rp.PublicId AS ReplyToId
        public DateTimeOffset CreatedAt { get; init; }
        public DateTime? EditedAt { get; init; }
        public DateTime? DeletedAt { get; init; }
        public bool IsSystemMessage { get; init; }
    }
}
