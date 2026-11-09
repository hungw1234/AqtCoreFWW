using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Audit.Infrastructure.Persistence;

public sealed class AiOutputReviewRepository(AuditDbContext db) : IAiOutputReviewRepository
{
    public async Task AddAsync(AiOutputReview review, CancellationToken cancellationToken = default)
    {
        db.AiOutputReviews.Add(review);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<AiOutputReview?> GetByPromptTraceInternalIdAsync(long promptTraceInternalId, CancellationToken cancellationToken = default) =>
        db.AiOutputReviews.FirstOrDefaultAsync(r => r.PromptTraceId == promptTraceInternalId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
