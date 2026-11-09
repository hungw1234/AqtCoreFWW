using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Collaboration.Domain.Entities;

/// <summary>
/// Tracks threaded replies to a parent message within a conversation.
/// Maintains reply count and last reply timestamp for efficient list rendering.
///
/// T1 entity: AuditableEntity (long Id).
/// ParentMessageId: long (FK to T2 Message.Id internal PK — changed from Guid).
/// ConversationId: long (FK to T2 Conversation.Id internal PK — changed from Guid).
/// </summary>
public sealed class Thread : AuditableEntity
{
    /// <summary>FK to Message.Id (long, T2 internal PK — parent message of this thread).</summary>
    public long ParentMessageId { get; private set; }
    /// <summary>FK to Conversation.Id (long, T2 internal PK).</summary>
    public long ConversationId { get; private set; }
    public int ReplyCount { get; private set; }
    public DateTime? LastReplyAt { get; private set; }

    private Thread() { } // EF Core

    /// <summary>Factory — creates a new thread for a parent message.</summary>
    public static Thread Create(long parentMessageId, long conversationId)
    {
        var thread = new Thread
        {
            ParentMessageId = parentMessageId,
            ConversationId = conversationId,
            ReplyCount = 0
        };
        // No specific user context at creation — set system audit
        thread.SetAuditCreate(Guid.Empty);
        return thread;
    }

    /// <summary>Increments reply count and updates last reply timestamp.</summary>
    public void IncrementReplyCount(DateTime replyAt)
    {
        ReplyCount++;
        LastReplyAt = replyAt;
        MarkUpdated();
    }
}
