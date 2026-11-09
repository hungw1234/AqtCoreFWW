using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Organization.Domain.Entities;

/// <summary>
/// Temporal assignment of a user to an org unit with a role.
/// ValidFrom/ValidTo enables assignment history tracking.
/// T1 entity: non-generic AuditableEntity (long Id DB-assigned).
///   OrgUnitId: long (FK to T1/T2 OrgUnit.Id).
///   UserId: Guid (FK exception — references AspNetUsers.Id).
///   TenantId: Guid (FK exception).
/// </summary>
public sealed class UserOrgUnitAssignment : AuditableEntity
{
    [DataClassification(DataClassificationLevel.Internal)]
    public Guid UserId { get; private set; }
    public long OrgUnitId { get; private set; }
    public string RoleInOrg { get; private set; } = string.Empty;
    public DateTimeOffset ValidFrom { get; private set; }
    public DateTimeOffset? ValidTo { get; private set; }
    public bool IsActive { get; private set; }
    public Guid TenantId { get; private set; }

    private UserOrgUnitAssignment() { }

    public static UserOrgUnitAssignment Create(
        Guid userId, long orgUnitId, string roleInOrg,
        Guid tenantId, DateTimeOffset? validFrom = null)
    {
        return new UserOrgUnitAssignment
        {
            UserId = userId,
            OrgUnitId = orgUnitId,
            RoleInOrg = roleInOrg,
            TenantId = tenantId,
            ValidFrom = validFrom ?? DateTimeOffset.UtcNow,
            IsActive = true
        };
    }

    public void Close(DateTimeOffset? closedAt = null)
    {
        ValidTo = closedAt ?? DateTimeOffset.UtcNow;
        IsActive = false;
        MarkUpdated();
    }
}
