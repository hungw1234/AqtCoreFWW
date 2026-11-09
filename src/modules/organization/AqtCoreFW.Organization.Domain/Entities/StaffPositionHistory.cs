using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Organization.Domain.Entities;

/// <summary>
/// Immutable record of a staff member's position in an org unit over time.
/// Written on assign and transfer; never updated — append-only history.
/// T1 entity: non-generic AuditableEntity (long Id DB-assigned).
///   OrgUnitId: long (FK to T1/T2 OrgUnit.Id).
///   UserId: Guid (FK exception — references AspNetUsers.Id).
///   TenantId: Guid (FK exception).
/// </summary>
public sealed class StaffPositionHistory : AuditableEntity
{
    [DataClassification(DataClassificationLevel.Internal)]
    public Guid UserId { get; private set; }
    public long OrgUnitId { get; private set; }
    [DataClassification(DataClassificationLevel.Confidential)]
    public string PositionTitle { get; private set; } = string.Empty;
    public DateTimeOffset StartDate { get; private set; }
    public DateTimeOffset? EndDate { get; private set; }
    public Guid TenantId { get; private set; }

    private StaffPositionHistory() { }

    public static StaffPositionHistory Create(
        Guid userId, long orgUnitId, string positionTitle,
        Guid tenantId, DateTimeOffset? startDate = null)
    {
        return new StaffPositionHistory
        {
            UserId = userId,
            OrgUnitId = orgUnitId,
            PositionTitle = positionTitle,
            TenantId = tenantId,
            StartDate = startDate ?? DateTimeOffset.UtcNow
        };
    }

    public void Close(DateTimeOffset endDate)
    {
        EndDate = endDate;
        MarkUpdated();
    }
}
