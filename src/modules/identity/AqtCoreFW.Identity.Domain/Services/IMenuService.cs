namespace AqtCoreFW.Identity.Domain.Services;

/// <summary>
/// Builds the authorized menu tree for a user based on their effective permission codes.
/// Results are cached in Redis per tenant (key: menu-tree:{tenantId}, TTL 30 min).
/// Invalidate cache when menu structure or permissions change.
/// </summary>
public interface IMenuService
{
    /// <summary>
    /// Returns the filtered menu tree containing only items the user is permitted to see.
    /// Uses any-of match: a menu item is included if the user holds at least one
    /// of its required permission codes. Menu items with no permission requirements
    /// are visible to all authenticated users.
    /// </summary>
    Task<IReadOnlyList<MenuNode>> GetMenuTreeAsync(
        IReadOnlySet<string> userPermissionCodes,
        CancellationToken ct = default);
}

/// <summary>A single node in the authorized menu tree returned to the client.</summary>
public sealed class MenuNode
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Icon { get; init; }
    public string? Route { get; init; }
    public int SortOrder { get; init; }
    public IReadOnlyList<MenuNode> Children { get; init; } = [];
}
