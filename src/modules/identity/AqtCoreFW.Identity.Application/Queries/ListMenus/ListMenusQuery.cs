using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.ListMenus;

/// <summary>List all menus (admin view — unfiltered by permission).</summary>
public sealed record ListMenusQuery(Guid? TenantId) : IQuery<IReadOnlyList<MenuDto>>;

public sealed record MenuDto(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Title,
    string? Icon,
    string? Route,
    int SortOrder,
    bool IsActive,
    Guid? TenantId,
    IReadOnlyList<string> PermissionCodes);
