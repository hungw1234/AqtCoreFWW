using AqtCoreFW.SharedKernel.Application.Data;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.GetRolePermissions;

public sealed class GetRolePermissionsQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetRolePermissionsQuery, Result<IReadOnlyList<RolePermissionDto>>>
{
    public async Task<Result<IReadOnlyList<RolePermissionDto>>> Handle(
        GetRolePermissionsQuery request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.Id AS PermissionId, p.Code, p.Name
            FROM [identity].RolePermissions rp
            JOIN [identity].Permissions p ON p.Id = rp.PermissionId
            WHERE rp.RoleId = @RoleId
            ORDER BY p.ModuleCode, p.ResourceCode, p.ActionCode
            """;

        var permissions = (await db.QueryAsync<RolePermissionDto>(
            sql,
            new { request.RoleId },
            cancellationToken: cancellationToken)).ToList();

        return Result.Ok<IReadOnlyList<RolePermissionDto>>(permissions);
    }
}
