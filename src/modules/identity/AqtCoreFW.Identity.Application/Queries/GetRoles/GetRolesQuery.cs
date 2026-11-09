using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.GetRoles;

/// <summary>Returns all roles from AspNetRoles with tenant filtering.</summary>
public sealed record GetRolesQuery(Guid? TenantId = null) : IQuery<IReadOnlyList<RoleDefinitionDto>>;

/// <summary>Role DTO — CLASS (not positional record) per Dapper convention [RT-04].</summary>
public sealed class RoleDefinitionDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public int RoleType { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public Guid? TenantId { get; init; }
}
