namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>Fine-grained permission within a module (RBAC leaf node).</summary>
public class Permission
{
    public Guid Id { get; set; }

    /// <summary>Stable code in format MODULE.RESOURCE.ACTION, e.g. "HOSO.HOSO.APPROVE". Unique.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Module owning this permission, e.g. "HOSO". Replaces legacy ModuleName.</summary>
    public string ModuleCode { get; set; } = string.Empty;

    /// <summary>Resource within module, e.g. "HOSO".</summary>
    public string ResourceCode { get; set; } = string.Empty;

    /// <summary>Action on the resource, e.g. "APPROVE".</summary>
    public string ActionCode { get; set; } = string.Empty;

    /// <summary>True = sensitive permission; access is audit-logged.</summary>
    public bool IsSensitive { get; set; }

    // Navigation
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
