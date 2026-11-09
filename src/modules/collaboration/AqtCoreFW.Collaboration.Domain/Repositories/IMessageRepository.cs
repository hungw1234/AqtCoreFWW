using AqtCoreFW.Collaboration.Domain.Entities;

namespace AqtCoreFW.Collaboration.Domain.Repositories;

/// <summary>Write-side repository for Message aggregate (T2: Guid PublicId AK).</summary>
public interface IMessageRepository
{
    /// <summary>Gets message by PublicId (T2 AK — the Guid exposed to FE).</summary>
    Task<Message?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken = default);

    /// <summary>Gets message by internal long Id (for infra/internal cross-entity lookups).</summary>
    Task<Message?> GetByInternalIdAsync(long id, CancellationToken cancellationToken = default);

    Task AddAsync(Message message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes pending changes on the tracked aggregate to the store. Load-then-mutate handlers
    /// (e.g. soft-delete) call this after mutating an entity returned by a Get* method — those
    /// entities are tracked (TrackingQuery), so a flush is all that is needed (no re-attach).
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns messages for a conversation by internal conversation long Id, newest first, paginated.</summary>
    Task<IReadOnlyList<Message>> GetByConversationInternalIdAsync(
        long conversationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
