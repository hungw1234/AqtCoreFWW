using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Ai.Application.Queries.GetChatHistory;

/// <summary>
/// Dapper read-path — bypasses EF/repository for optimal query performance.
/// Returns messages newest-first, paginated.
/// </summary>
public sealed class GetChatHistoryQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetChatHistoryQuery, Result<IReadOnlyList<ChatMessageDto>>>
{
    public async Task<Result<IReadOnlyList<ChatMessageDto>>> Handle(
        GetChatHistoryQuery request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                m.Id           AS MessageId,
                m.Role,
                m.Content,
                m.ContentHash,
                m.IsContentEncrypted,
                m.CreatedAt
            FROM ai.AiChatMessages m
            INNER JOIN ai.AiSessions s ON s.Id = m.SessionId
            WHERE m.SessionId = @SessionId
              AND s.TenantId  = @TenantId
              AND m.IsDeleted = 0
            ORDER BY m.CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var rows = await db.QueryAsync<ChatMessageDto>(sql, new
        {
            request.SessionId,
            request.TenantId,
            Offset = (page - 1) * pageSize,
            PageSize = pageSize
        });

        return Result.Ok<IReadOnlyList<ChatMessageDto>>(rows.ToList());
    }
}
