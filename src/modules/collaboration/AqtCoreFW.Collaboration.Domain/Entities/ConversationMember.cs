using AqtCoreFW.Collaboration.Domain.Enums;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Collaboration.Domain.Entities;

/// <summary>
/// Represents a user's membership in a conversation, including role, mute status,
/// and last-read tracking for unread count calculation.
///
/// T1 entity: AuditableEntity (long Id).
/// ConversationId: long (FK to T2 Conversation.Id internal PK).
/// UserId: Guid (FK exception list — identity ref).
/// LastReadMessageId: long? (FK to T2 Message.Id internal PK — changed from Guid).
/// </summary>
public sealed class ConversationMember : AuditableEntity
{
    /// <summary>FK to Conversation.Id (long, T2 internal PK).</summary>
    public long ConversationId { get; private set; }
    [DataClassification(DataClassificationLevel.Internal)]
    public Guid UserId { get; private set; }
    public DateTime JoinedAt { get; private set; }
    public MemberRole Role { get; private set; }
    public DateTime? MutedUntil { get; private set; }
    /// <summary>FK to Message.Id (long, T2 internal PK — last message user has read).</summary>
    public long? LastReadMessageId { get; private set; }

    private ConversationMember() { } // EF Core

    /// <summary>Factory — creates a new conversation member.</summary>
    public static ConversationMember Create(long conversationId, Guid userId, MemberRole role)
    {
        var member = new ConversationMember
        {
            ConversationId = conversationId,
            UserId = userId,
            JoinedAt = DateTime.UtcNow,
            Role = role
        };
        member.SetAuditCreate(userId);
        return member;
    }

    public void Mute(DateTime until)
    {
        MutedUntil = until;
        MarkUpdated();
    }

    public void Unmute()
    {
        MutedUntil = null;
        MarkUpdated();
    }

    /// <summary>Updates last-read pointer by internal message PK.</summary>
    public void UpdateLastRead(long messageId)
    {
        LastReadMessageId = messageId;
        MarkUpdated();
    }
}
