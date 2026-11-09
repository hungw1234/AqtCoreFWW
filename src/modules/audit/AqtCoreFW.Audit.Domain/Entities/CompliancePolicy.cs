using AqtCoreFW.Audit.Domain.Enums;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Audit.Domain.Entities;

/// <summary>
/// Defines a governance rule set for a compliance domain — M15 AI Governance.
/// Rules stored as JSON for flexible evaluation logic.
/// Enforcement: Audit = log-only; Block = reject the triggering operation.
/// T1: long PK (DB-assigned, via non-generic AuditableEntity). IAggregateRoot retains manual event list.
/// </summary>
public sealed class CompliancePolicy : AuditableEntity, IAggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public ComplianceCategory Category { get; private set; }

    /// <summary>JSON-serialised rule definitions evaluated by EvaluateCompliancePolicyHandler.</summary>
    public string Rules { get; private set; } = string.Empty;

    public PolicyEnforcement Enforcement { get; private set; }
    public bool IsEnabled { get; private set; }

    // IAggregateRoot support
    private readonly List<SharedKernel.Domain.Events.IDomainEvent> _domainEvents = [];
    public IReadOnlyCollection<SharedKernel.Domain.Events.IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(SharedKernel.Domain.Events.IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    private CompliancePolicy() { }

    public static CompliancePolicy Create(
        string name,
        ComplianceCategory category,
        string rules,
        PolicyEnforcement enforcement)
    {
        return new CompliancePolicy
        {
            Name = name,
            Category = category,
            Rules = rules,
            Enforcement = enforcement,
            IsEnabled = true
        };
    }

    public void Enable() => IsEnabled = true;

    public void Disable() => IsEnabled = false;

    public void UpdateRules(string json)
    {
        Rules = json;
        MarkUpdated();
    }
}
