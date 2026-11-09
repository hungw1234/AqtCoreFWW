using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.Collaboration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Collaboration.Infrastructure.Repositories;

/// <summary>EF Core implementation of IMessageRepository (T2: Guid PublicId AK).</summary>
public sealed class MessageRepository(CollaborationDbContext db) : IMessageRepository
{
    /// <inheritdoc/>
    public async Task<Message?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken = default) =>
        await db.TrackingQuery<Message>()
            .FirstOrDefaultAsync(m => m.PublicId == publicId, cancellationToken);

    /// <inheritdoc/>
    public async Task<Message?> GetByInternalIdAsync(long id, CancellationToken cancellationToken = default) =>
        await db.TrackingQuery<Message>()
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task AddAsync(Message message, CancellationToken cancellationToken = default)
    {
        await db.Messages.AddAsync(message, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyList<Message>> GetByConversationInternalIdAsync(
        long conversationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        await db.Query<Message>()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
}
