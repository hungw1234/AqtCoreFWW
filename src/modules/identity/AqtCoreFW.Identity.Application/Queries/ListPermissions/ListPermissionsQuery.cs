using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.ListPermissions;

public sealed record ListPermissionsQuery : IQuery<IReadOnlyList<PermissionDto>>;

/// <summary>Permission DTO — class per Dapper convention.</summary>
public sealed class PermissionDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ModuleCode { get; init; } = string.Empty;
    public string ResourceCode { get; init; } = string.Empty;
    public string ActionCode { get; init; } = string.Empty;
    public bool IsSensitive { get; init; }
}
