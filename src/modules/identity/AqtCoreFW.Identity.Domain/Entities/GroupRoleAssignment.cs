namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Junction: a group is assigned a role (SEC_GroupRole table).
/// Unique constraint on (GroupId, RoleId) — one assignment per group per role.
/// </summary>
public class GroupRoleAssignment
{
    public Guid Id { get; set; }
    /// <summary>FK to UserGroup.Id (long — T2 entity with bigint PK post-refactor).</summary>
    public long GroupId { get; set; }
    public Guid RoleId { get; set; }
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UserId of the admin who created this assignment.</summary>
    public Guid AssignedBy { get; set; }

    // Navigation
    public UserGroup Group { get; set; } = null!;
    public ApplicationRole Role { get; set; } = null!;
}
