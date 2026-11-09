using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Audit.Infrastructure.Persistence;

public sealed class AiPromptTraceRepository(AuditDbContext db) : IAiPromptTraceRepository
{
    public async Task AddAsync(AiPromptTrace trace, CancellationToken cancellationToken = default)
    {
        db.AiPromptTraces.Add(trace);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<AiPromptTrace?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.AiPromptTraces.FirstOrDefaultAsync(t => t.MessageId == id, cancellationToken);
}
