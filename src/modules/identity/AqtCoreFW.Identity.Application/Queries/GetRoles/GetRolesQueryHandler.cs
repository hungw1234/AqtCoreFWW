using AqtCoreFW.SharedKernel.Application.Data;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.GetRoles;

/// <summary>
/// Returns all roles from AspNetRoles table with tenant filtering [RT-07].
/// </summary>
public sealed class GetRolesQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetRolesQuery, Result<IReadOnlyList<RoleDefinitionDto>>>
{
    public async Task<Result<IReadOnlyList<RoleDefinitionDto>>> Handle(
        GetRolesQuery request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, Name, Code, RoleType, Description, IsActive, TenantId
            FROM [identity].AspNetRoles
            WHERE TenantId = @TenantId OR TenantId IS NULL
            ORDER BY Name
            """;

        var roles = (await db.QueryAsync<RoleRow>(
            sql,
            new { request.TenantId },
            cancellationToken: cancellationToken))
            .Select(r => new RoleDefinitionDto
            {
                Id = r.Id,
                Name = r.Name,
                Code = r.Code,
                RoleType = r.RoleType,
                Description = r.Description,
                IsActive = r.IsActive,
                TenantId = r.TenantId
            })
            .ToList();

        return Result.Ok<IReadOnlyList<RoleDefinitionDto>>(roles);
    }

    /// <summary>Dapper mapping row — class per RT-04.</summary>
    internal sealed class RoleRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public int RoleType { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public Guid? TenantId { get; set; }
    }
}
