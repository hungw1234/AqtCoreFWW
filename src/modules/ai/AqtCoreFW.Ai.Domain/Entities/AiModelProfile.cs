using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Ai.Domain.Entities;

/// <summary>
/// AI model profile — stores configuration for an AI provider/model pair.
/// IsDefault: exactly one profile per tenant should be default.
/// IsActive: deactivated profiles are excluded from routing decisions.
///
/// T2 entity: AuditableEntity (long Id internal) + IPublicIdentified (Guid PublicId AK external).
/// </summary>
public sealed class AiModelProfile : AuditableEntity, IAggregateRoot, IPublicIdentified
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Public alternate key for external addressing (API URLs, DTOs). Set by factory via IGuidGenerator.NewIdMonotonic().</summary>
    public Guid PublicId { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public AiProvider Provider { get; private set; }
    public string ModelId { get; private set; } = string.Empty;
    /// <summary>AI provider endpoint URL — may contain credentials; treat as Internal.</summary>
    [DataClassification(DataClassificationLevel.Internal)]
    public string? Endpoint { get; private set; }
    public int MaxTokens { get; private set; }
    public decimal Temperature { get; private set; }
    public bool IsDefault { get; private set; }
    public decimal CostPerToken { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    // EF constructor
    private AiModelProfile() { }

    public static AiModelProfile Create(
        IGuidGenerator gen,
        string name,
        AiProvider provider,
        string modelId,
        string? endpoint,
        int maxTokens,
        decimal temperature,
        decimal costPerToken,
        bool isDefault = false)
    {
        ArgumentNullException.ThrowIfNull(gen);
        return new AiModelProfile
        {
            PublicId = gen.NewIdMonotonic(),
            Name = name,
            Provider = provider,
            ModelId = modelId,
            Endpoint = endpoint,
            MaxTokens = maxTokens,
            Temperature = temperature,
            CostPerToken = costPerToken,
            IsDefault = isDefault,
            IsActive = true
        };
    }

    /// <summary>Marks this profile as the system default.</summary>
    public void SetAsDefault()
    {
        IsDefault = true;
        MarkUpdated();
    }

    /// <summary>Clears the default flag (called before promoting another profile).</summary>
    public void ClearDefault()
    {
        IsDefault = false;
        MarkUpdated();
    }

    /// <summary>Soft-deactivates — excluded from routing after this call.</summary>
    public void Deactivate()
    {
        IsActive = false;
        IsDefault = false;
        MarkUpdated();
    }

    /// <summary>Updates inference configuration parameters.</summary>
    public void UpdateConfig(int maxTokens, decimal temperature)
    {
        MaxTokens = maxTokens;
        Temperature = temperature;
        MarkUpdated();
    }
}
