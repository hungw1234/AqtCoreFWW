using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Ai.Domain.Entities;

/// <summary>
/// Reusable prompt template — system + user prompt pair versioned for audit.
/// Version increments on every prompt update (immutable audit trail).
///
/// T2 entity: AuditableEntity (long Id internal) + IPublicIdentified (Guid PublicId AK external).
/// </summary>
public sealed class AiPromptTemplate : AuditableEntity, IAggregateRoot, IPublicIdentified
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Public alternate key for external addressing (API URLs, DTOs). Set by factory via IGuidGenerator.NewIdMonotonic().</summary>
    public Guid PublicId { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public PromptCategory Category { get; private set; }
    public string SystemPrompt { get; private set; } = string.Empty;
    public string UserPromptTemplate { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    // EF constructor
    private AiPromptTemplate() { }

    public static AiPromptTemplate Create(
        IGuidGenerator gen,
        string name,
        PromptCategory category,
        string systemPrompt,
        string userPromptTemplate)
    {
        ArgumentNullException.ThrowIfNull(gen);
        return new AiPromptTemplate
        {
            PublicId = gen.NewIdMonotonic(),
            Name = name,
            Category = category,
            SystemPrompt = systemPrompt,
            UserPromptTemplate = userPromptTemplate,
            Version = 1,
            IsActive = true
        };
    }

    /// <summary>Increments version — call after any prompt content change.</summary>
    public void IncrementVersion()
    {
        Version++;
        MarkUpdated();
    }

    /// <summary>Deactivates template — excluded from template resolution after this call.</summary>
    public void Deactivate()
    {
        IsActive = false;
        MarkUpdated();
    }

    /// <summary>Updates prompt content and auto-increments version.</summary>
    public void UpdatePrompts(string systemPrompt, string userPromptTemplate)
    {
        SystemPrompt = systemPrompt;
        UserPromptTemplate = userPromptTemplate;
        IncrementVersion();
    }
}
