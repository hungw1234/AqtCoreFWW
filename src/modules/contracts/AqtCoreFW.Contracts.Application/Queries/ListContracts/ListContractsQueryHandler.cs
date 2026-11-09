using AqtCoreFW.Contracts.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Contracts.Application.Queries.ListContracts;

public sealed class ListContractsQueryHandler(IReadDbConnection db)
    : IRequestHandler<ListContractsQuery, Result<IReadOnlyList<ContractDto>>>
{
    public async Task<Result<IReadOnlyList<ContractDto>>> Handle(
        ListContractsQuery request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, TenantId, ContractNumber, Title, CounterpartyName,
                   StartDate, EndDate, Status, Description, CreatedAt
            FROM [contracts].Contracts
            WHERE TenantId = @TenantId AND IsDeleted = 0
            ORDER BY ContractNumber
            """;

        var contracts = await db.QueryAsync<ContractDto>(
            sql, new { request.TenantId }, cancellationToken);
        return Result.Ok<IReadOnlyList<ContractDto>>(contracts.ToList());
    }
}
