using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Collaboration.Domain.Repositories;
using AqtCoreFW.Collaboration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Collaboration.Infrastructure.Repositories;

/// <summary>EF Core implementation of IMessageReadStateRepository.</summary>
public sealed class MessageReadStateRepository(CollaborationDbContext db) : IMessageReadStateRepository
{
    /// <inheritdoc/>
    public async Task<bool> AddIfNotExistsAsync(
        MessageReadState readState, CancellationToken cancellationToken = default)
    {
        var exists = await db.MessageReadStates
            .AsNoTracking()
            .AnyAsync(r => r.MessageId == readState.MessageId && r.UserId == readState.UserId, cancellationToken);
        if (exists)
            return false;

        await db.MessageReadStates.AddAsync(readState, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // A concurrent first-read inserted the same (MessageId, UserId) between the check above and
            // this save; the unique index rejected ours. Detach the rejected entity so the context stays
            // clean and report "already existed" — the receipt is present either way.
            db.Entry(readState).State = EntityState.Detached;
            return false;
        }
    }
}
