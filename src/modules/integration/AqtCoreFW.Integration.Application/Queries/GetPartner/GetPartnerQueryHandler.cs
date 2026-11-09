using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Integration.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Integration.Application.Queries.GetPartner;

public sealed class GetPartnerQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetPartnerQuery, Result<PartnerDto>>
{
    public async Task<Result<PartnerDto>> Handle(
        GetPartnerQuery request, CancellationToken cancellationToken)
    {
        // T2: SELECT PublicId AS Id (Guid AK → PartnerDto.Id Guid).
        const string sql = """
            SELECT PublicId AS Id, TenantId, Name, Code, ContactEmail, ContactPhone,
                   Endpoint, AuthScheme, Status, CreatedAt, UpdatedAt
            FROM [integration].Partners
            WHERE PublicId = @PublicId AND TenantId = @TenantId AND IsDeleted = 0
            """;

        var row = await db.QueryFirstOrDefaultAsync<PartnerDto>(
            sql, new { request.PublicId, request.TenantId }, cancellationToken);

        return row is null
            ? Result.Fail(new NotFoundError($"Partner {request.PublicId} not found."))
            : Result.Ok(row);
    }
}
