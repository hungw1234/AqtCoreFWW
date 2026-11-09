using AqtCoreFW.AssetManagement.Application.DTOs;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.SharedKernel.Application.Data;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.AssetManagement.Application.Queries.GetAssetManagement;

public sealed class GetAssetManagementQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetAssetManagementQuery, Result<AssetManagementDto>>
{
    public async Task<Result<AssetManagementDto>> Handle(
        GetAssetManagementQuery request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, TenantId, Title, Description, CreatedAt
            FROM [assetmanagement].AssetManagements
            WHERE Id = @Id AND TenantId = @TenantId AND IsDeleted = 0
            """;

        var dto = await db.QueryFirstOrDefaultAsync<AssetManagementDto>(
            sql, new { request.Id, request.TenantId }, cancellationToken);

        return dto is not null
            ? Result.Ok(dto)
            : Result.Fail<AssetManagementDto>(
                new NotFoundError($"Không tìm thấy tài sản '{request.Id}'."));
    }
}
