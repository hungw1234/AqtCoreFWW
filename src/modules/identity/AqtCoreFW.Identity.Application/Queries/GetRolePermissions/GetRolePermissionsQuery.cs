using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.GetRolePermissions;

public sealed record GetRolePermissionsQuery(Guid RoleId) : IQuery<IReadOnlyList<RolePermissionDto>>;

/// <summary>Assigned permission DTO — class per Dapper convention.</summary>
public sealed class RolePermissionDto
{
    public Guid PermissionId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}
