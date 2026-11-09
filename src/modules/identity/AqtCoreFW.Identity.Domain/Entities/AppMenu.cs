using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Application menu item — supports hierarchical structure via ParentId self-reference.
/// Access is controlled by MenuRolePermission entries: a user sees a menu item
/// if they hold at least one of its required permission codes.
/// T2: long PK (Id, DB-assigned) + Guid AK (PublicId, set at create via IGuidGenerator v7).
/// </summary>
public sealed class AppMenu : AuditableEntity, IPublicIdentified
{
    private AppMenu() { }

    /// <summary>Public-facing alternate key (Guid v7 monotonic). Used in API URLs and DTOs.</summary>
    public Guid PublicId { get; private set; }

    /// <summary>Parent menu item public ID — null means top-level.</summary>
    public Guid? ParentId { get; private set; }

    /// <summary>Stable machine-readable code, unique per tenant.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Display title shown in the navigation UI.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Optional tooltip description shown on hover.</summary>
    public string? Description { get; private set; }

    /// <summary>Optional icon identifier (e.g., Ant Design icon name).</summary>
    public string? Icon { get; private set; }

    /// <summary>Frontend route path (e.g., "/admin/users").</summary>
    public string? Route { get; private set; }

    /// <summary>Display order within the same parent level (ascending).</summary>
    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; } = true;

    public Guid? TenantId { get; private set; }

    // Navigation
    public ICollection<MenuRolePermission> RolePermissions { get; private set; } = [];

    /// <summary>Factory — creates a new AppMenu item.</summary>
    public static AppMenu Create(
        IGuidGenerator gen,
        string code,
        string title,
        Guid? parentId,
        string? description,
        string? icon,
        string? route,
        int sortOrder,
        Guid? tenantId,
        Guid actorId)
    {
        var menu = new AppMenu
        {
            PublicId = gen.NewIdMonotonic(),
            Code = code.Trim(),
            Title = title.Trim(),
            ParentId = parentId,
            Description = description?.Trim(),
            Icon = icon?.Trim(),
            Route = route?.Trim(),
            SortOrder = sortOrder,
            IsActive = true,
            TenantId = tenantId,
        };
        menu.SetAuditCreate(actorId);
        return menu;
    }

    /// <summary>Updates mutable display properties.</summary>
    public void Update(
        string title,
        string? description,
        string? icon,
        string? route,
        int sortOrder,
        Guid actorId)
    {
        Title = title.Trim();
        Description = description?.Trim();
        Icon = icon?.Trim();
        Route = route?.Trim();
        SortOrder = sortOrder;
        SetAuditUpdate(actorId);
    }

    /// <summary>Reassigns the parent menu item (null = promote to top-level).</summary>
    public void UpdateParentId(Guid? parentId, Guid actorId)
    {
        ParentId = parentId;
        SetAuditUpdate(actorId);
    }

    public void Deactivate(Guid actorId)
    {
        IsActive = false;
        SetAuditUpdate(actorId);
    }

    public void Activate(Guid actorId)
    {
        IsActive = true;
        SetAuditUpdate(actorId);
    }
}
