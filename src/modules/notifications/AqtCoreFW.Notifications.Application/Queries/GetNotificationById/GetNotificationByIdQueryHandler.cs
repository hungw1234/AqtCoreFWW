using FluentResults;
using MediatR;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Notifications.Application.DTOs;

namespace AqtCoreFW.Notifications.Application.Queries.GetNotificationById;

public sealed class GetNotificationByIdQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetNotificationByIdQuery, Result<NotificationDto>>
{
    public async Task<Result<NotificationDto>> Handle(
        GetNotificationByIdQuery request,
        CancellationToken cancellationToken)
    {
        // T2: project PublicId AS Id — DTO.Id exposes external Guid (PublicId), not internal long
        const string sql = """
            SELECT PublicId AS Id, TenantId, RecipientUserId, Subject, Body, Channel,
                   IsRead, ReadAt, CreatedAt
            FROM notifications.Notifications
            WHERE PublicId = @Id AND TenantId = @TenantId AND IsDeleted = 0
            """;

        var dto = await db.QueryFirstOrDefaultAsync<NotificationDto>(
            sql, new { request.Id, request.TenantId }, cancellationToken);

        return dto is null
            ? Result.Fail(new NotFoundError($"Notification {request.Id} not found."))
            : Result.Ok(dto);
    }
}
