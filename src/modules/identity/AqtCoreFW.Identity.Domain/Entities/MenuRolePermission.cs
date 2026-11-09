namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Join entity — links a menu item to a permission code required to see it.
/// A user is granted access to an AppMenu when they hold at least one
/// of its associated PermissionCodes (any-of match).
/// Composite PK: (MenuId, PermissionCode).
/// </summary>
public class MenuRolePermission
{
    /// <summary>FK to AppMenu.Id (long — T2 entity with bigint PK post-refactor).</summary>
    public long MenuId { get; set; }

    /// <summary>Permission code required to view this menu item.</summary>
    public string PermissionCode { get; set; } = string.Empty;

    // Navigation
    public AppMenu Menu { get; set; } = null!;
}
