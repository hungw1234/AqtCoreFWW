using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Ai.Domain.Enums;
using AqtCoreFW.Ai.Domain.Repositories;
using AqtCoreFW.Ai.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Ai.Infrastructure.Repositories;

/// <summary>EF Core implementation of IAiPromptTemplateRepository. No tenant filter — AiPromptTemplate is global per-app.</summary>
public sealed class AiPromptTemplateRepository(AiDbContext db) : IAiPromptTemplateRepository
{
    public Task<AiPromptTemplate?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default) =>
        db.AiPromptTemplates.FirstOrDefaultAsync(t => t.PublicId == publicId, ct);

    public Task<AiPromptTemplate?> GetByInternalIdAsync(long internalId, CancellationToken ct = default) =>
        db.AiPromptTemplates.FirstOrDefaultAsync(t => t.Id == internalId, ct);

    public Task<List<AiPromptTemplate>> GetActiveAsync(
        PromptCategory? category = null,
        CancellationToken ct = default)
    {
        var query = db.AiPromptTemplates.Where(t => t.IsActive);

        if (category.HasValue)
            query = query.Where(t => t.Category == category.Value);

        return query.ToListAsync(ct);
    }

    public async Task AddAsync(AiPromptTemplate template, CancellationToken ct = default) =>
        await db.AiPromptTemplates.AddAsync(template, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}
