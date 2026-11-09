using AqtCoreFW.AssetManagement.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.AssetManagement.Application.Queries.ListAssetManagement;

public sealed class ListAssetManagementQueryHandler(IReadDbConnection db)
    : IRequestHandler<ListAssetManagementQuery, Result<IReadOnlyList<AssetManagementDto>>>
{
    public async Task<Result<IReadOnlyList<AssetManagementDto>>> Handle(
        ListAssetManagementQuery request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, TenantId, Title, Description, CreatedAt
            FROM [assetmanagement].AssetManagements
            WHERE TenantId = @TenantId AND IsDeleted = 0
            ORDER BY Id
            """;

        var rows = await db.QueryAsync<AssetManagementDto>(
            sql, new { request.TenantId }, cancellationToken);

        return Result.Ok<IReadOnlyList<AssetManagementDto>>(rows.ToList());
    }
}
