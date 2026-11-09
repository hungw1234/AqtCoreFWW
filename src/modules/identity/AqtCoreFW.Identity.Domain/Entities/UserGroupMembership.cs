namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Junction: user belongs to a group (SEC_UserGroup table).
/// Unique constraint on (UserId, GroupId) — one membership record per user per group.
/// </summary>
public class UserGroupMembership
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>FK to UserGroup.Id (long — T2 entity with bigint PK post-refactor).</summary>
    public long GroupId { get; set; }
    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UserId of the admin who added this member.</summary>
    public Guid AddedBy { get; set; }

    // Navigation
    public ApplicationUser User { get; set; } = null!;
    public UserGroup Group { get; set; } = null!;
}
