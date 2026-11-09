using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.GetRoleDataScopes;

/// <summary>Get all data scope assignments for a specific role.</summary>
public sealed record GetRoleDataScopesQuery(Guid RoleId) : IQuery<IReadOnlyList<RoleDataScopeDto>>;

public sealed record RoleDataScopeDto(
    Guid Id,
    Guid RoleId,
    Guid DataScopeTypeId,
    string DataScopeTypeCode,
    string? ScopeField,
    string? ScopeValue,
    int Priority);
