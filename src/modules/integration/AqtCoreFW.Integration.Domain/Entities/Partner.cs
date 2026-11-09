using AqtCoreFW.Integration.Domain.Enums;
using AqtCoreFW.Integration.Domain.Events;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Integration.Domain.Entities;

/// <summary>
/// GOV partner aggregate root — tracks integration partner lifecycle.
/// State transitions: Active ↔ Suspended → Deactivated.
///
/// T2 entity: AuditableEntity (long Id internal) + IPublicIdentified (Guid PublicId AK external).
/// Retiered T1→T2 per Phase-07 — IAggregateRoot + lifecycle = T2 profile.
/// TenantId: Guid (FK exception list — tenant isolation key).
/// Events carry PublicId (Guid AK) as PartnerPublicId — stable across exports.
/// </summary>
public sealed class Partner : AuditableEntity, IAggregateRoot, IPublicIdentified, ITenantScoped
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Public alternate key for external addressing (API URLs, DTOs). Set by factory via IGuidGenerator.NewIdMonotonic().</summary>
    public Guid PublicId { get; private set; }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string? ContactEmail { get; private set; }
    public string? ContactPhone { get; private set; }
    public string? Endpoint { get; private set; }
    public string? AuthScheme { get; private set; }
    public PartnerStatus Status { get; private set; } = PartnerStatus.Active;

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private Partner() { } // EF Core

    /// <summary>
    /// Factory method — creates an Active partner.
    /// <paramref name="gen"/> must be non-null; injects PublicId (T2 Guid AK) via IGuidGenerator.NewIdMonotonic().
    /// </summary>
    public static Partner Create(IGuidGenerator gen, Guid tenantId, string name, string code, Guid createdBy,
        string? contactEmail = null, string? contactPhone = null,
        string? endpoint = null, string? authScheme = null)
    {
        ArgumentNullException.ThrowIfNull(gen);
        var partner = new Partner
        {
            PublicId = gen.NewIdMonotonic(),
            TenantId = tenantId,
            Name = name,
            Code = code,
            ContactEmail = contactEmail,
            ContactPhone = contactPhone,
            Endpoint = endpoint,
            AuthScheme = authScheme,
            Status = PartnerStatus.Active
        };
        partner.SetAuditCreate(createdBy);
        partner.AddDomainEvent(new PartnerCreatedEvent(partner.PublicId, tenantId, gen.NewIdRandom()));
        return partner;
    }

    public void Update(string name, string code, string? contactEmail, string? contactPhone,
        string? endpoint, string? authScheme, Guid modifiedBy)
    {
        Name = name;
        Code = code;
        ContactEmail = contactEmail;
        ContactPhone = contactPhone;
        Endpoint = endpoint;
        AuthScheme = authScheme;
        SetAuditUpdate(modifiedBy);
    }

    public void Suspend()
    {
        if (Status != PartnerStatus.Active)
            throw new InvalidOperationException($"Cannot suspend partner in {Status} state.");
        Status = PartnerStatus.Suspended;
    }

    public void Activate()
    {
        if (Status != PartnerStatus.Suspended)
            throw new InvalidOperationException($"Cannot activate partner in {Status} state.");
        Status = PartnerStatus.Active;
    }

    public void Deactivate(IGuidGenerator gen)
    {
        if (Status == PartnerStatus.Deactivated)
            throw new InvalidOperationException("Partner already deactivated.");
        Status = PartnerStatus.Deactivated;
        AddDomainEvent(new PartnerDeactivatedEvent(PublicId, TenantId, gen.NewIdRandom()));
    }

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
