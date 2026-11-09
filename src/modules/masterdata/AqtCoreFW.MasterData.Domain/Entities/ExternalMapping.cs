using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.MasterData.Domain.Entities;

/// <summary>Directionality of the code mapping between internal and external systems.</summary>
public enum MappingDirection
{
    In = 1,
    Out = 2,
    Both = 3
}

/// <summary>
/// Maps internal dictionary codes to codes used by external systems (e.g., VBQPPL, eMIS, VNPT).
/// Supports time-bound validity and bidirectional/unidirectional translation.
/// </summary>
public class ExternalMapping : AuditableEntity, ITenantScoped, IPublicIdentified
{
    /// <summary>Public-facing AK (POST returns this Guid). AMEND-A13 re-tier T1→T2.</summary>
    public Guid PublicId { get; private set; }

    public string InternalCode { get; private set; } = string.Empty;
    public string ExternalSystem { get; private set; } = string.Empty;
    public string ExternalCode { get; private set; } = string.Empty;
    public MappingDirection Direction { get; private set; }

    /// <summary>Optional link to the source dictionary (null = ad-hoc mapping). FK to Dictionary.Id (long).</summary>
    public long? DictionaryId { get; private set; }

    public bool IsActive { get; private set; }
    public DateTime ValidFrom { get; private set; }
    public DateTime? ValidTo { get; private set; }
    public Guid TenantId { get; private set; }

    // Navigation
    public Dictionary? Dictionary { get; private set; }

    private ExternalMapping() { }

    public static ExternalMapping Create(
        IGuidGenerator gen,
        string internalCode, string externalSystem, string externalCode,
        MappingDirection direction, long? dictionaryId,
        DateTime validFrom, DateTime? validTo,
        Guid tenantId, Guid createdBy)
    {
        var mapping = new ExternalMapping
        {
            PublicId = gen.NewIdRandom(),
            InternalCode = internalCode,
            ExternalSystem = externalSystem,
            ExternalCode = externalCode,
            Direction = direction,
            DictionaryId = dictionaryId,
            IsActive = true,
            ValidFrom = validFrom,
            ValidTo = validTo,
            TenantId = tenantId
        };
        mapping.SetAuditCreate(createdBy);
        return mapping;
    }

    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetAuditUpdate(modifiedBy);
    }
}
