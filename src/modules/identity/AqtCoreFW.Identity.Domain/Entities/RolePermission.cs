namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>Join table: ApplicationRole ↔ Permission (many-to-many).</summary>
public class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }

    // Navigation
    public ApplicationRole Role { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
}
