using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Integration.Application.DTOs;
using AqtCoreFW.Integration.Domain.Enums;
using AqtCoreFW.Integration.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Integration.Application.Commands.UpdateMessageLogStatus;

public sealed class UpdateMessageLogStatusCommandHandler(
    IMessageLogRepository repository,
    IReadDbConnection db)
    : IRequestHandler<UpdateMessageLogStatusCommand, Result<MessageLogDto>>
{
    public async Task<Result<MessageLogDto>> Handle(
        UpdateMessageLogStatusCommand request, CancellationToken cancellationToken)
    {
        // T3 entity: request.Id is MessageId (Guid AK) — use GetByMessageIdAsync.
        var log = await repository.GetByMessageIdAsync(request.Id, cancellationToken);
        if (log is null)
            return Result.Fail(new NotFoundError($"MessageLog {request.Id} not found."));

        switch (request.NewStatus)
        {
            case MessageLogStatus.Delivered:    log.MarkDelivered(); break;
            case MessageLogStatus.Failed:       log.MarkFailed();    break;
            case MessageLogStatus.Acknowledged: log.Acknowledge();   break;
            default:
                return Result.Fail(new ValidationError($"Cannot transition to {request.NewStatus}."));
        }

        await repository.UpdateAsync(log, cancellationToken);

        // Post-update: project Guid AKs via JOIN (MessageLog stores long FKs internally).
        // T3: Id = MessageId (Guid AK). PartnerId = Partner.PublicId (Guid). ContractId = Contract.PublicId (Guid?).
        const string sql = """
            SELECT ml.MessageId AS Id, ml.TenantId,
                   p.PublicId AS PartnerId, c.PublicId AS ContractId,
                   ml.Direction, ml.MessageType, ml.Payload, ml.Status,
                   ml.CorrelationId, ml.SentAt, ml.AcknowledgedAt,
                   ml.CreatedAt, CAST(NULL AS datetime2) AS UpdatedAt
            FROM [integration].MessageLogs ml
            JOIN [integration].Partners p ON p.Id = ml.PartnerId
            LEFT JOIN [integration].Contracts c ON c.Id = ml.ContractId
            WHERE ml.MessageId = @Id
            """;

        var dto = await db.QueryFirstOrDefaultAsync<MessageLogDto>(
            sql, new { request.Id }, cancellationToken);

        return dto is null
            ? Result.Fail(new NotFoundError($"MessageLog {request.Id} not found after update."))
            : Result.Ok(dto);
    }
}
