using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Logical group of users that can be assigned roles in bulk (SEC_Group table).
/// Groups are tenant-scoped; Code is unique within a tenant.
/// T2: long PK (Id, DB-assigned) + Guid AK (PublicId, set at create via IGuidGenerator v7).
/// </summary>
public sealed class UserGroup : AuditableEntity, IPublicIdentified
{
    private UserGroup() { }

    /// <summary>Public-facing alternate key (Guid v7 monotonic). Used in API URLs and DTOs.</summary>
    public Guid PublicId { get; private set; }

    /// <summary>Stable machine-readable code, e.g. "PHONG_TIEP_NHAN". Unique per tenant.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;
    public Guid? TenantId { get; private set; }

    // Navigation
    public ICollection<UserGroupMembership> Members { get; private set; } = new List<UserGroupMembership>();
    public ICollection<GroupRoleAssignment> RoleAssignments { get; private set; } = new List<GroupRoleAssignment>();

    /// <summary>Factory — creates a new user group.</summary>
    public static UserGroup Create(
        IGuidGenerator gen,
        string code,
        string name,
        string? description,
        Guid? tenantId,
        Guid actorId)
    {
        var group = new UserGroup
        {
            PublicId = gen.NewIdMonotonic(),
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Description = description?.Trim(),
            IsActive = true,
            TenantId = tenantId,
        };
        group.SetAuditCreate(actorId);
        return group;
    }

    /// <summary>Updates mutable display properties.</summary>
    public void Update(string name, string? description, Guid actorId)
    {
        Name = name.Trim();
        Description = description?.Trim();
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
