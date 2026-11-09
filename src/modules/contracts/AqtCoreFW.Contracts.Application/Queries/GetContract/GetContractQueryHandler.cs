using AqtCoreFW.Contracts.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using Dapper;
using FluentResults;
using MediatR;
using AqtCoreFW.SharedKernel.Errors;

namespace AqtCoreFW.Contracts.Application.Queries.GetContract;

public sealed class GetContractQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetContractQuery, Result<ContractDto>>
{
    public async Task<Result<ContractDto>> Handle(
        GetContractQuery request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, TenantId, ContractNumber, Title, CounterpartyName,
                   StartDate, EndDate, Status, Description, CreatedAt
            FROM [contracts].Contracts
            WHERE Id = @Id AND TenantId = @TenantId AND IsDeleted = 0
            """;

        var contract = await db.QueryFirstOrDefaultAsync<ContractDto>(
            sql, new { request.Id, request.TenantId }, cancellationToken);

        return contract is not null
            ? Result.Ok(contract)
            : Result.Fail<ContractDto>(new NotFoundError($"Không tìm thấy hợp đồng '{request.Id}'."));
    }
}
