using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Ai.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Ai.Infrastructure.Persistence;

/// <summary>EF Core repository for AiSession + AiChatMessage aggregates.</summary>
public sealed class AiSessionRepository(AiDbContext db) : IAiSessionRepository
{
    public async Task<AiSession?> GetByIdAsync(long sessionId, CancellationToken ct = default)
        => await db.AiSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    public async Task<IReadOnlyList<AiSession>> GetByUserAsync(
        Guid userId, Guid tenantId, CancellationToken ct = default)
        => await db.AiSessions
            .Where(s => s.UserId == userId && s.TenantId == tenantId)
            .OrderByDescending(s => s.LastActivityAt)
            .ToListAsync(ct);

    public async Task AddAsync(AiSession session, CancellationToken ct = default)
        => await db.AiSessions.AddAsync(session, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await db.SaveChangesAsync(ct);
}
