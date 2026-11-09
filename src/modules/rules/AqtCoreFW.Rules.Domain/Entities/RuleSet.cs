using AqtCoreFW.Rules.Domain.Enums;
using AqtCoreFW.Rules.Domain.Events;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Rules.Domain.Entities;

/// <summary>
/// RuleSet aggregate root — owns a versioned set of evaluation rules.
/// Status transitions: Draft → Active → Deprecated.
/// Each activation snapshots current rules into a RuleVersion.
/// T2 entity: AuditableEntity + IPublicIdentified (PublicId = v7 monotonic Guid AK).
/// TenantId: Guid (FK exception list — tenant isolation ref).
/// </summary>
public sealed class RuleSet : AuditableEntity, IAggregateRoot, ITenantScoped, IPublicIdentified
{
    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly List<Rule> _rules = [];
    private readonly List<RuleVersion> _versions = [];

    public Guid PublicId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? Category { get; private set; }
    public int Version { get; private set; } = 1;
    public RuleSetStatus Status { get; private set; } = RuleSetStatus.Draft;
    public string? TargetEntityType { get; private set; }

    public IReadOnlyList<Rule> Rules => _rules.AsReadOnly();
    public IReadOnlyList<RuleVersion> Versions => _versions.AsReadOnly();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private RuleSet() { } // EF Core

    /// <summary>Factory — creates a new Draft RuleSet.</summary>
    public static RuleSet Create(
        IGuidGenerator gen,
        string name,
        Guid tenantId,
        Guid createdBy,
        string? description = null,
        string? category = null,
        string? targetEntityType = null)
    {
        var ruleSet = new RuleSet
        {
            PublicId = gen.NewIdMonotonic(),
            Name = name,
            TenantId = tenantId,
            Description = description,
            Category = category,
            TargetEntityType = targetEntityType,
            Status = RuleSetStatus.Draft,
            Version = 1
        };
        ruleSet.SetAuditCreate(createdBy);
        return ruleSet;
    }

    /// <summary>Transitions status to Active and raises RuleSetActivatedEvent.</summary>
    public void Activate(IGuidGenerator gen)
    {
        if (Status == RuleSetStatus.Deprecated)
            throw new InvalidOperationException("Cannot activate a deprecated RuleSet.");

        Status = RuleSetStatus.Active;
        // Use PublicId (Guid AK) in event payload — not internal long Id.
        AddDomainEvent(new RuleSetActivatedEvent(PublicId, TenantId, gen.NewIdMonotonic()));
    }

    /// <summary>Marks this RuleSet as superseded.</summary>
    public void Deprecate()
    {
        if (Status == RuleSetStatus.Draft)
            throw new InvalidOperationException("Cannot deprecate a Draft RuleSet directly.");

        Status = RuleSetStatus.Deprecated;
    }

    /// <summary>Increments the semantic version number.</summary>
    public void IncrementVersion() => Version++;

    /// <summary>Adds a rule (called during domain operations, not EF navigation load).</summary>
    public void AddRule(Rule rule) => _rules.Add(rule);

    /// <summary>Adds a version snapshot (called by ActivateRuleVersion handler).</summary>
    public void AddVersion(RuleVersion version) => _versions.Add(version);

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
