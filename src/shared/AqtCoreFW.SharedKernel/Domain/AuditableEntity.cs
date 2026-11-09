namespace AqtCoreFW.SharedKernel.Domain;

/// <summary>
/// Extends Entity with user audit trail (Guid CreatedBy/ModifiedBy = FK to ApplicationUser.Id) and QĐ742 data classification.
/// Phase-2 Foundation Refactor (2026-05-03): collapsed generic; Entity is now non-generic with long Id.
/// CreatedBy/ModifiedBy stay Guid (FK exception list — references AspNetUsers.Id which is framework-managed Guid PK).
/// </summary>
public abstract class AuditableEntity : Entity
{
    public Guid CreatedBy { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    /// <summary>Data classification per QĐ742: Public | Internal | Confidential | Secret | TopSecret.</summary>
    public ClassificationLevel ClassificationLevel { get; protected set; } = ClassificationLevel.Internal;

    public void SetAuditCreate(Guid userId) => CreatedBy = userId;

    public void SetAuditUpdate(Guid userId)
    {
        ModifiedBy = userId;
        MarkUpdated();
    }
}
