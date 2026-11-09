using AqtCoreFW.Integration.Domain.Entities;
using AqtCoreFW.Integration.Domain.Repositories;
using AqtCoreFW.Integration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Integration.Infrastructure.Repositories;

public sealed class MessageLogRepository(IntegrationDbContext dbContext) : IMessageLogRepository
{
    public async Task<MessageLog?> GetByMessageIdAsync(Guid messageId, CancellationToken ct)
        => await dbContext.MessageLogs.FirstOrDefaultAsync(m => m.MessageId == messageId, ct);

    public async Task<MessageLog?> GetByInternalIdAsync(long id, CancellationToken ct)
        => await dbContext.MessageLogs.FirstOrDefaultAsync(m => m.InternalId == id, ct);

    public async Task AddAsync(MessageLog log, CancellationToken ct)
    {
        dbContext.MessageLogs.Add(log);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(MessageLog log, CancellationToken ct)
    {
        if (dbContext.Entry(log).State == EntityState.Detached)
            dbContext.MessageLogs.Update(log);
        await dbContext.SaveChangesAsync(ct);
    }
}
