using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>Classifies a role as system-reserved or business-defined.</summary>
public enum RoleType
{
    /// <summary>Built-in system role — cannot be deleted or renamed by tenants.</summary>
    System = 1,

    /// <summary>Tenant-defined business role.</summary>
    Business = 2
}

/// <summary>Extended IdentityRole with stable code, type classification, and tenant scoping.</summary>
public class ApplicationRole : IdentityRole<Guid>
{
    /// <summary>Stable machine-readable code, e.g. "CHUYEN_VIEN_XU_LY". Unique per tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public RoleType RoleType { get; set; } = RoleType.Business;

    public string? Description { get; set; }
    public Guid? TenantId { get; set; }

    /// <summary>Soft-disable flag — deactivated roles still exist but grant no access.</summary>
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<GroupRoleAssignment> GroupRoles { get; set; } = new List<GroupRoleAssignment>();
}
