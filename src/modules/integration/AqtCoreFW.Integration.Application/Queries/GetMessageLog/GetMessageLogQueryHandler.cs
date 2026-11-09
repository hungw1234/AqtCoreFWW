using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Integration.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Integration.Application.Queries.GetMessageLog;

public sealed class GetMessageLogQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetMessageLogQuery, Result<MessageLogDto>>
{
    public async Task<Result<MessageLogDto>> Handle(
        GetMessageLogQuery request, CancellationToken cancellationToken)
    {
        // T3: SELECT ml.MessageId AS Id (Guid AK → MessageLogDto.Id Guid).
        // PartnerId projected as Guid: JOIN Partners p ON p.Id = ml.PartnerId → p.PublicId AS PartnerId.
        // ContractId projected as Guid?: LEFT JOIN Contracts c ON c.Id = ml.ContractId → c.PublicId AS ContractId.
        // No UpdatedAt on T3 plain-class — project typed NULL (CAST AS datetime2 so Dapper infers DateTime, not INT).
        const string sql = """
            SELECT ml.MessageId AS Id, ml.TenantId,
                   p.PublicId AS PartnerId, c.PublicId AS ContractId,
                   ml.Direction, ml.MessageType, ml.Payload, ml.Status,
                   ml.CorrelationId, ml.SentAt, ml.AcknowledgedAt,
                   ml.CreatedAt, CAST(NULL AS datetime2) AS UpdatedAt
            FROM [integration].MessageLogs ml
            JOIN [integration].Partners p ON p.Id = ml.PartnerId
            LEFT JOIN [integration].Contracts c ON c.Id = ml.ContractId
            WHERE ml.MessageId = @Id AND ml.TenantId = @TenantId
            """;

        var row = await db.QueryFirstOrDefaultAsync<MessageLogDto>(
            sql, new { request.Id, request.TenantId }, cancellationToken);

        return row is null
            ? Result.Fail(new NotFoundError($"MessageLog {request.Id} not found."))
            : Result.Ok(row);
    }
}
