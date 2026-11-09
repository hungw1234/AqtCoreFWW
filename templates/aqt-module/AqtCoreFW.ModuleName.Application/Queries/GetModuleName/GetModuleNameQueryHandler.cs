using AqtCoreFW.ModuleName.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.ModuleName.Application.Queries.GetModuleName;

public sealed class GetModuleNameQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetModuleNameQuery, Result<ModuleNameDto>>
{
    public async Task<Result<ModuleNameDto>> Handle(
        GetModuleNameQuery request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, TenantId, Title, Description, CreatedAt
            FROM [modulename].ModuleNames
            WHERE Id = @Id AND TenantId = @TenantId AND IsDeleted = 0
            """;

        var dto = await db.QueryFirstOrDefaultAsync<ModuleNameDto>(
            sql, new { request.Id, request.TenantId }, cancellationToken);

        return dto is not null
            ? Result.Ok(dto)
            : Result.Fail($"Không tìm thấy dữ liệu '{request.Id}'.");
    }
}
