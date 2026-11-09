using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.ListGroups;

/// <summary>List all user groups, optionally filtered by tenant.</summary>
public sealed record ListGroupsQuery(Guid? TenantId) : IQuery<IReadOnlyList<GroupDto>>;

public sealed record GroupDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    Guid? TenantId,
    DateTime CreatedAtUtc);
