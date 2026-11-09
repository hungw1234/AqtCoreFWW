using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Collaboration.Domain.Entities;

/// <summary>
/// Per-message read receipt: records that a specific user has read a specific message (the "seen by"
/// feature). NOTE: unread COUNTS run off ConversationMember.LastReadMessageId, not this table — this
/// records individual receipts for display, written by MarkAsReadCommandHandler.
/// Unique constraint on (MessageId, UserId) enforced via EF config (idempotent re-reads).
///
/// T1 entity: Entity (long Id).
/// MessageId: long (FK to T2 Message.Id internal PK — changed from Guid).
/// UserId: Guid (FK exception list — identity ref).
/// </summary>
public sealed class MessageReadState : Entity
{
    /// <summary>FK to Message.Id (long, T2 internal PK).</summary>
    public long MessageId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime ReadAt { get; private set; }

    private MessageReadState() { } // EF Core

    /// <summary>Factory — marks a message as read by a user.</summary>
    public static MessageReadState Create(long messageId, Guid userId)
    {
        return new MessageReadState
        {
            MessageId = messageId,
            UserId = userId,
            ReadAt = DateTime.UtcNow
        };
    }
}
