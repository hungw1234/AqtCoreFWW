using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Events;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Collaboration.Domain.Entities;

/// <summary>
/// Conversation aggregate root — represents a Direct, Group, or Channel chat session.
/// Raises ConversationCreatedEvent on creation.
///
/// T2 entity: AuditableEntity + IPublicIdentified (PublicId = v7 monotonic Guid AK).
/// TenantId: Guid (FK exception list — tenant isolation key).
/// CreatedBy: Guid (FK exception list — identity ref via SetAuditCreate).
/// Phase-02c: ConversationCreatedEvent MessageId caller-supplied via gen (Rule1 compliant).
/// </summary>
public sealed class Conversation : AuditableEntity, IPublicIdentified, IAggregateRoot, ITenantScoped
{
    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly List<ConversationMember> _members = [];

    public Guid PublicId { get; private set; }
    public Guid TenantId { get; private set; }
    public ConversationType Type { get; private set; }
    public string? Name { get; private set; }
    public DateTime? LastMessageAt { get; private set; }
    public bool IsArchived { get; private set; }

    public IReadOnlyList<ConversationMember> Members => _members.AsReadOnly();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private Conversation() { } // EF Core

    /// <summary>Factory — creates a new conversation and raises ConversationCreatedEvent.</summary>
    public static Conversation Create(
        IGuidGenerator gen,
        ConversationType type,
        string? name,
        Guid createdBy,
        Guid tenantId)
    {
        var conversation = new Conversation
        {
            PublicId = gen.NewIdMonotonic(),
            Type = type,
            Name = name,
            TenantId = tenantId,
            IsArchived = false
        };
        conversation.SetAuditCreate(createdBy);
        // Event carries PublicId (T2 AK) + Guid identity refs (FK exception). MessageId from gen (Rule1 compliant).
        conversation.AddDomainEvent(new ConversationCreatedEvent(conversation.PublicId, createdBy, tenantId, gen.NewIdRandom()));
        return conversation;
    }

    /// <summary>Adds a member to the conversation — tracked by EF via backing _members list.</summary>
    public void AddMember(ConversationMember member) => _members.Add(member);

    public void Archive()
    {
        if (IsArchived) return;
        IsArchived = true;
        MarkUpdated();
    }

    public void Unarchive()
    {
        if (!IsArchived) return;
        IsArchived = false;
        MarkUpdated();
    }

    public void UpdateLastMessageAt(DateTime messageAt)
    {
        LastMessageAt = messageAt;
        MarkUpdated();
    }

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
