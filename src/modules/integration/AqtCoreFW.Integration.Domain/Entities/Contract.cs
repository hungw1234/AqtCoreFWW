using AqtCoreFW.Integration.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Integration.Domain.Entities;

/// <summary>
/// Integration contract — defines data scope and validity period for a partner relationship.
/// State transitions: Draft → Active → Expired | Terminated.
///
/// T2 entity: AuditableEntity + IPublicIdentified (PublicId = v7 monotonic Guid AK).
/// TenantId: Guid (FK exception list — tenant isolation key).
/// CreatedBy: Guid (FK exception list — identity ref via SetAuditCreate).
/// PartnerId: long (FK to T1 Partner.Id internal PK — changed from Guid).
/// </summary>
public sealed class Contract : AuditableEntity, IPublicIdentified, ITenantScoped
{
    public Guid PublicId { get; private set; }
    public Guid TenantId { get; private set; }
    /// <summary>FK to Partner.Id (long, T1 internal PK).</summary>
    public long PartnerId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTime EffectiveDate { get; private set; }
    public DateTime? ExpiryDate { get; private set; }
    public ContractStatus Status { get; private set; } = ContractStatus.Draft;
    public string? DataScopeJson { get; private set; }

    private Contract() { } // EF Core

    public static Contract Create(IGuidGenerator gen, Guid tenantId, long partnerId, string title,
        string? description, DateTime effectiveDate, DateTime? expiryDate,
        string? dataScopeJson, Guid createdBy)
    {
        var contract = new Contract
        {
            PublicId = gen.NewIdMonotonic(),
            TenantId = tenantId,
            PartnerId = partnerId,
            Title = title,
            Description = description,
            EffectiveDate = effectiveDate,
            ExpiryDate = expiryDate,
            DataScopeJson = dataScopeJson,
            Status = ContractStatus.Draft
        };
        contract.SetAuditCreate(createdBy);
        return contract;
    }

    public void Update(string title, string? description, DateTime effectiveDate,
        DateTime? expiryDate, string? dataScopeJson, Guid modifiedBy)
    {
        Title = title;
        Description = description;
        EffectiveDate = effectiveDate;
        ExpiryDate = expiryDate;
        DataScopeJson = dataScopeJson;
        SetAuditUpdate(modifiedBy);
    }

    public void Activate()
    {
        if (Status != ContractStatus.Draft)
            throw new InvalidOperationException($"Cannot activate contract in {Status} state.");
        Status = ContractStatus.Active;
    }

    public void Terminate()
    {
        if (Status is not (ContractStatus.Draft or ContractStatus.Active))
            throw new InvalidOperationException($"Cannot terminate contract in {Status} state.");
        Status = ContractStatus.Terminated;
    }

    public void MarkExpired()
    {
        if (Status != ContractStatus.Active)
            throw new InvalidOperationException($"Cannot expire contract in {Status} state.");
        Status = ContractStatus.Expired;
    }
}
