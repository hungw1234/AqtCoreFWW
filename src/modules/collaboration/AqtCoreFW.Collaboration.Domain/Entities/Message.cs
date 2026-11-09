using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.Collaboration.Domain.Events;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Collaboration.Domain.Entities;

/// <summary>
/// Message aggregate root — represents a single message in a conversation.
/// Supports editing, soft-delete, reply threading, and system messages.
/// Raises MessageSentEvent on creation.
///
/// T2 entity: AuditableEntity + IPublicIdentified (PublicId = v7 monotonic Guid AK).
/// ConversationId: long (FK to T2 Conversation.Id internal PK).
/// ReplyToId: long? (FK to self — internal PK of the replied-to message).
/// TenantId/SenderId: Guid (FK exception list — tenant isolation + identity refs).
/// Phase-02c: MessageSentEvent MessageId caller-supplied via gen (Rule1 compliant).
/// </summary>
public sealed class Message : AuditableEntity, IPublicIdentified, IAggregateRoot, ITenantScoped
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid PublicId { get; private set; }
    public Guid TenantId { get; private set; }
    /// <summary>FK to Conversation.Id (long, T2 internal PK).</summary>
    public long ConversationId { get; private set; }
    [DataClassification(DataClassificationLevel.Internal)]
    public Guid SenderId { get; private set; }
    /// <summary>Message content — may contain PII entered by users (names, contact details).</summary>
    [DataClassification(DataClassificationLevel.Confidential)]
    public string Content { get; private set; } = string.Empty;
    public ContentType ContentType { get; private set; }
    /// <summary>FK to Message.Id (long, self-referential — internal PK of the replied-to message).</summary>
    public long? ReplyToId { get; private set; }
    public DateTime? EditedAt { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public bool IsSystemMessage { get; private set; }
    public string? Metadata { get; private set; }

    /// <summary>True when the message has been soft-deleted via SoftDelete().</summary>
    public bool IsMessageDeleted => DeletedAt.HasValue;

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private Message() { } // EF Core

    /// <summary>Factory — creates a new message and raises MessageSentEvent.</summary>
    public static Message Create(
        IGuidGenerator gen,
        long conversationId,
        Guid senderId,
        string content,
        ContentType contentType,
        Guid tenantId,
        long? replyToId = null,
        string? metadata = null)
    {
        var message = new Message
        {
            PublicId = gen.NewIdMonotonic(),
            ConversationId = conversationId,
            SenderId = senderId,
            Content = content,
            ContentType = contentType,
            TenantId = tenantId,
            ReplyToId = replyToId,
            Metadata = metadata,
            IsSystemMessage = contentType == ContentType.System
        };
        message.SetAuditCreate(senderId);
        // Event carries PublicId (T2 AK) + long internal ConversationId + Guid identity refs (FK exception). MessageId from gen (Rule1 compliant).
        message.AddDomainEvent(new MessageSentEvent(message.PublicId, conversationId, senderId, tenantId, gen.NewIdRandom()));
        return message;
    }

    /// <summary>Edits message content. Only allowed on non-deleted messages.</summary>
    public void Edit(string newContent)
    {
        if (IsMessageDeleted)
            throw new InvalidOperationException("Cannot edit a deleted message.");

        Content = newContent;
        EditedAt = DateTime.UtcNow;
        MarkUpdated();
    }

    /// <summary>Soft-deletes the message by setting DeletedAt timestamp.</summary>
    public void SoftDelete()
    {
        if (IsMessageDeleted) return;
        DeletedAt = DateTime.UtcNow;
        MarkUpdated();
    }

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
