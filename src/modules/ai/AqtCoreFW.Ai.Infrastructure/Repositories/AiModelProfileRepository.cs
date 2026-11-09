using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Ai.Domain.Repositories;
using AqtCoreFW.Ai.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Ai.Infrastructure.Repositories;

/// <summary>EF Core implementation of IAiModelProfileRepository (T2 — global resource, no tenant filter).</summary>
public sealed class AiModelProfileRepository(AiDbContext db) : IAiModelProfileRepository
{
    public Task<AiModelProfile?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default) =>
        db.AiModelProfiles.FirstOrDefaultAsync(p => p.PublicId == publicId, ct);

    public Task<AiModelProfile?> GetByInternalIdAsync(long internalId, CancellationToken ct = default) =>
        db.AiModelProfiles.FirstOrDefaultAsync(p => p.Id == internalId, ct);

    public Task<AiModelProfile?> GetDefaultAsync(CancellationToken ct = default) =>
        db.AiModelProfiles.FirstOrDefaultAsync(p => p.IsDefault && p.IsActive, ct);

    public Task<List<AiModelProfile>> GetActiveAsync(CancellationToken ct = default) =>
        db.AiModelProfiles.Where(p => p.IsActive).ToListAsync(ct);

    public async Task AddAsync(AiModelProfile profile, CancellationToken ct = default) =>
        await db.AiModelProfiles.AddAsync(profile, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}
