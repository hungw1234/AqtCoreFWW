using FluentResults;
using MediatR;
using AqtCoreFW.SharedKernel.Application.Data;

namespace AqtCoreFW.Notifications.Application.Queries.GetUnreadNotificationCount;

public sealed class GetUnreadNotificationCountQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetUnreadNotificationCountQuery, Result<int>>
{
    public async Task<Result<int>> Handle(
        GetUnreadNotificationCountQuery request, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM notifications.Notifications
            WHERE RecipientUserId = @UserId AND TenantId = @TenantId
              AND IsRead = 0 AND IsDeleted = 0
            """;

        var count = await db.QueryFirstOrDefaultAsync<int>(
            sql, new { request.UserId, request.TenantId }, cancellationToken);

        return Result.Ok(count);
    }
}
