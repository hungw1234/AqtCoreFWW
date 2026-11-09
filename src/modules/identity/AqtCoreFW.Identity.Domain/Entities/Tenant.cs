using AqtCoreFW.Identity.Domain.Events;
using AqtCoreFW.Identity.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Tenant aggregate root — canonical tenant entity in Identity module.
/// Code is immutable post-create (downstream FK references). Name/ContactEmail/Status mutable.
/// RowVersion provides optimistic concurrency (EF interceptor).
/// T2: long PK (Id, DB-assigned) + Guid AK (PublicId, set at create via IGuidGenerator v7).
/// </summary>
public sealed class Tenant : AuditableEntity, IPublicIdentified
{
    /// <summary>EF Core parameterless constructor.</summary>
    private Tenant() { }

    /// <summary>Public-facing alternate key (Guid v7 monotonic). Used in API URLs and DTOs.</summary>
    public Guid PublicId { get; private set; }

    /// <summary>Tenant display name (e.g. "Bộ Tài chính").</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Unique immutable code (e.g. "TENANT-ABC12345").
    /// Cannot be changed after creation — used as stable reference key.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Optional contact email for tenant administration.</summary>
    public string? ContactEmail { get; private set; }

    /// <summary>Lifecycle status: Active, Suspended, or Archived.</summary>
    public TenantStatus Status { get; private set; } = TenantStatus.Active;

    /// <summary>Concurrency token — EF Core RowVersion.</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Domain events raised by this aggregate.</summary>
    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Factory method — creates a new Tenant with Active status.
    /// Raises TenantCreatedEvent with PublicId as tenant identifier.
    /// </summary>
    public static Tenant Create(
        IGuidGenerator gen,
        string name,
        string code,
        string? contactEmail,
        Guid actorId)
    {
        var tenant = new Tenant
        {
            PublicId = gen.NewIdMonotonic(),
            Name = name.Trim(),
            Code = code.Trim().ToUpperInvariant(),
            ContactEmail = contactEmail?.Trim(),
            Status = TenantStatus.Active
        };
        tenant.SetAuditCreate(actorId);
        tenant._domainEvents.Add(new TenantCreatedEvent(tenant.PublicId, tenant.Name, tenant.Code, gen.NewIdRandom()));
        return tenant;
    }

    /// <summary>
    /// Updates mutable properties (Name, ContactEmail). Code is IMMUTABLE.
    /// </summary>
    public void Update(string name, string? contactEmail, Guid actorId)
    {
        Name = name.Trim();
        ContactEmail = contactEmail?.Trim();
        SetAuditUpdate(actorId);
    }

    /// <summary>
    /// Changes tenant status (e.g. Suspend, Archive, Reactivate).
    /// Raises TenantStatusChangedEvent with PublicId as tenant identifier.
    /// </summary>
    public void ChangeStatus(TenantStatus newStatus, Guid actorId, IGuidGenerator gen)
    {
        if (Status == newStatus) return;

        var oldStatus = Status;
        Status = newStatus;
        SetAuditUpdate(actorId);
        _domainEvents.Add(new TenantStatusChangedEvent(PublicId, oldStatus, newStatus, actorId, gen.NewIdRandom()));
    }
}
