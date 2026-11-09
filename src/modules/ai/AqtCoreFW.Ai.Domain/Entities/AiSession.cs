using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Ai.Domain.Entities;

/// <summary>
/// AI conversation session — groups messages for a user+tenant pair.
/// All AI interactions retained for GOV audit (full retention policy).
///
/// T1 entity: Entity (long Id). TenantId/UserId: Guid (FK exception list — tenant + identity refs).
/// AiChatMessage.SessionId: long (FK to this entity's internal PK).
/// </summary>
public sealed class AiSession : Entity
{
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public int MessageCount { get; private set; }

    private readonly List<AiChatMessage> _messages = [];
    public IReadOnlyList<AiChatMessage> Messages => _messages;

    // EF constructor
    private AiSession() { }

    public static AiSession Create(Guid tenantId, Guid userId)
    {
        var session = new AiSession
        {
            TenantId = tenantId,
            UserId = userId,
            StartedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow,
            MessageCount = 0
        };
        return session;
    }

    /// <summary>Add a plain-text message (Public/Internal classification).</summary>
    public AiChatMessage AddMessage(AqtCoreFW.Ai.Domain.Enums.ChatRole role, string content)
    {
        // Pass long Id (DB-assigned); 0 pre-SaveChanges — caller must save session first.
        var message = AiChatMessage.Create(Id, role, content);
        _messages.Add(message);
        MessageCount++;
        LastActivityAt = DateTimeOffset.UtcNow;
        MarkUpdated();
        return message;
    }

    /// <summary>Add a confidential message — stores hash only, no raw content (Confidential+).</summary>
    public AiChatMessage AddConfidentialMessage(AqtCoreFW.Ai.Domain.Enums.ChatRole role, string content)
    {
        var message = AiChatMessage.CreateConfidential(Id, role, content);
        _messages.Add(message);
        MessageCount++;
        LastActivityAt = DateTimeOffset.UtcNow;
        MarkUpdated();
        return message;
    }
}
