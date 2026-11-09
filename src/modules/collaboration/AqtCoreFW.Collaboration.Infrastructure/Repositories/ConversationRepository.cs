using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.Collaboration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Collaboration.Infrastructure.Repositories;

/// <summary>EF Core implementation of IConversationRepository (T2: Guid PublicId AK).</summary>
public sealed class ConversationRepository(CollaborationDbContext db) : IConversationRepository
{
    /// <inheritdoc/>
    public async Task<Conversation?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken = default) =>
        await db.TrackingQuery<Conversation>()
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.PublicId == publicId, cancellationToken);

    /// <inheritdoc/>
    public async Task<Conversation?> GetByInternalIdAsync(long id, CancellationToken cancellationToken = default) =>
        await db.TrackingQuery<Conversation>()
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        await db.Conversations.AddAsync(conversation, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task<Conversation?> FindDirectBetweenAsync(
        Guid user1, Guid user2, Guid tenantId, CancellationToken cancellationToken = default) =>
        await db.Conversations
            .Include(c => c.Members)
            .Where(c => c.Type == Domain.Enums.ConversationType.Direct
                && !c.IsArchived
                && c.TenantId == tenantId
                && c.Members.Any(m => m.UserId == user1)
                && c.Members.Any(m => m.UserId == user2))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<bool> IsMemberAsync(
        Guid conversationPublicId, Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
        // IgnoreQueryFilters because the caller (a SignalR hub invocation) has no reliable ambient
        // tenant — ITenantContext resolves off HttpContext, which is absent here. Disabling the global
        // filters means BOTH tenant AND soft-delete must be re-applied by hand: tenant + !IsDeleted on
        // the Conversation, and !IsDeleted on the joined Members (a removed member must not pass). The
        // explicit c.TenantId == tenantId predicate is what makes this tenant-safe, and membership is
        // itself tenant-bound (a user is never a member of another tenant's conversation).
        await db.Conversations
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => c.PublicId == conversationPublicId && c.TenantId == tenantId && !c.IsDeleted)
            .SelectMany(c => c.Members)
            .AnyAsync(m => m.UserId == userId && !m.IsDeleted, cancellationToken);
}
