using FluentResults;
using MediatR;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Notifications.Application.DTOs;

namespace AqtCoreFW.Notifications.Application.Queries.GetNotificationTemplateById;

public sealed class GetNotificationTemplateByIdQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetNotificationTemplateByIdQuery, Result<NotificationTemplateDto>>
{
    public async Task<Result<NotificationTemplateDto>> Handle(
        GetNotificationTemplateByIdQuery request,
        CancellationToken cancellationToken)
    {
        // T2: Id param is Guid PublicId AK — project PublicId as Id in SELECT
        const string sql = """
            SELECT PublicId AS Id, TenantId, TemplateKey, SubjectTemplate, BodyTemplate,
                   Channel, IsDefault, CreatedAt, UpdatedAt
            FROM notifications.NotificationTemplates
            WHERE PublicId = @Id AND IsDeleted = 0
            """;

        // CreatedAt/UpdatedAt are datetime2 columns → DateTimeOffset via the global DapperTypeHandlers
        // handler, so the DTO materializes directly (no intermediate Row record needed).
        var dto = await db.QueryFirstOrDefaultAsync<NotificationTemplateDto>(
            sql, new { request.Id }, cancellationToken);

        if (dto is null)
            return Result.Fail(new NotFoundError($"Notification template {request.Id} not found."));

        return Result.Ok(dto);
    }
}
