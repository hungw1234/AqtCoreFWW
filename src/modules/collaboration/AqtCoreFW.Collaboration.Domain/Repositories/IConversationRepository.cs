using AqtCoreFW.Collaboration.Domain.Entities;

namespace AqtCoreFW.Collaboration.Domain.Repositories;

/// <summary>Write-side repository for Conversation aggregate (T2: Guid PublicId AK).</summary>
public interface IConversationRepository
{
    /// <summary>Gets conversation by PublicId (T2 AK — the Guid exposed to FE), including Members navigation.</summary>
    Task<Conversation?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken = default);

    /// <summary>Gets conversation by internal long Id (for infra/internal cross-entity lookups).</summary>
    Task<Conversation?> GetByInternalIdAsync(long id, CancellationToken cancellationToken = default);

    Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes pending changes on the tracked aggregate to the store. Load-then-mutate handlers
    /// (archive, mark-read, add-member) call this after mutating an entity returned by a Get* method —
    /// those entities are tracked (TrackingQuery), so a flush is all that is needed (no re-attach).
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Find existing non-archived Direct conversation between two users.</summary>
    Task<Conversation?> FindDirectBetweenAsync(Guid user1, Guid user2, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True if <paramref name="userId"/> is a member of the conversation (PublicId) within
    /// <paramref name="tenantId"/>. Takes tenant explicitly so it is callable from contexts with no
    /// ambient tenant (e.g. a SignalR hub invocation, where ITenantContext has no HttpContext).
    /// </summary>
    Task<bool> IsMemberAsync(Guid conversationPublicId, Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
}
