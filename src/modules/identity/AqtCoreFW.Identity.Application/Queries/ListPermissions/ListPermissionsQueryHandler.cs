using AqtCoreFW.SharedKernel.Application.Data;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.ListPermissions;

public sealed class ListPermissionsQueryHandler(IReadDbConnection db)
    : IRequestHandler<ListPermissionsQuery, Result<IReadOnlyList<PermissionDto>>>
{
    public async Task<Result<IReadOnlyList<PermissionDto>>> Handle(
        ListPermissionsQuery request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, Code, Name, ModuleCode, ResourceCode, ActionCode, IsSensitive
            FROM [identity].Permissions
            ORDER BY ModuleCode, ResourceCode, ActionCode
            """;

        var permissions = (await db.QueryAsync<PermissionDto>(
            sql, cancellationToken: cancellationToken)).ToList();

        return Result.Ok<IReadOnlyList<PermissionDto>>(permissions);
    }
}
