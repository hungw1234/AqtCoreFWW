using AqtCoreFW.SharedKernel.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Infrastructure.Messaging;

/// <summary>
/// EF-backed <see cref="IProcessedMessageStore"/>. Claims a (consumer, key) pair by INSERTing a
/// <see cref="ProcessedMessage"/>; the UNIQUE index collapses concurrent/duplicate claims at the DB
/// engine — a unique-violation on SaveChanges is the "already processed" signal, so there is no
/// read-then-write TOCTOU race. Scoped: shares the consume scope's <see cref="MessagingDbContext"/>.
/// </summary>
public sealed class ProcessedMessageStore(MessagingDbContext db) : IProcessedMessageStore
{
    public async Task<bool> TryClaimAsync(string consumerName, string dedupKey, CancellationToken ct = default)
    {
        var row = new ProcessedMessage
        {
            ConsumerName   = consumerName,
            DedupKey       = dedupKey,
            ProcessedAtUtc = DateTimeOffset.UtcNow,
        };
        db.ProcessedMessages.Add(row);

        try
        {
            await db.SaveChangesAsync(ct);
            return true; // first claim — caller performs its effect
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Another delivery of this same message already claimed the pair → duplicate. Detach the
            // rejected insert so this shared scoped context stays usable for any later claim in the scope.
            db.Entry(row).State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>
    /// True when the exception wraps a SQL Server unique-index/constraint violation (2601 duplicate key
    /// in a unique index, 2627 unique constraint). Walks the inner-exception chain — mirrors the detection
    /// used by the signing session/workflow race handlers.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
            if (inner is SqlException { Number: 2601 or 2627 })
                return true;
        return false;
    }
}
