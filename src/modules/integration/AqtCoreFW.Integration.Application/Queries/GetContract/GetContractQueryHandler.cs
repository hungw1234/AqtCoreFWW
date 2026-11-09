using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Integration.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Integration.Application.Queries.GetContract;

public sealed class GetContractQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetContractQuery, Result<ContractDto>>
{
    public async Task<Result<ContractDto>> Handle(
        GetContractQuery request, CancellationToken cancellationToken)
    {
        // T2: SELECT c.PublicId AS Id (Guid AK → ContractDto.Id Guid).
        // PartnerId projected as Guid: JOIN Partners p ON p.Id = c.PartnerId → p.PublicId AS PartnerId.
        const string sql = """
            SELECT c.PublicId AS Id, c.TenantId, p.PublicId AS PartnerId, c.Title, c.Description,
                   c.EffectiveDate, c.ExpiryDate, c.Status, c.DataScopeJson,
                   c.CreatedAt, c.UpdatedAt
            FROM [integration].Contracts c
            JOIN [integration].Partners p ON p.Id = c.PartnerId
            WHERE c.PublicId = @Id AND c.TenantId = @TenantId AND c.IsDeleted = 0
            """;

        var row = await db.QueryFirstOrDefaultAsync<ContractDto>(
            sql, new { request.Id, request.TenantId }, cancellationToken);

        return row is null
            ? Result.Fail(new NotFoundError($"Contract {request.Id} not found."))
            : Result.Ok(row);
    }
}
