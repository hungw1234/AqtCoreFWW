using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Messaging;

/// <summary>
/// Admin service for dead letter management: list, inspect, retry, quarantine.
/// Retry repubs the original message via IPublishEndpoint — goes back to original exchange.
/// </summary>
public sealed class DeadLetterService(
    MessagingDbContext db,
    IPublishEndpoint publishEndpoint,
    ILogger<DeadLetterService> logger)
{
    public async Task<List<MessageDeadLetter>> ListAsync(
        DeadLetterStatus? status,
        string? messageType,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = db.DeadLetters.AsNoTracking();

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);
        if (!string.IsNullOrEmpty(messageType))
            query = query.Where(x => x.MessageType.Contains(messageType));
        if (from.HasValue)
            query = query.Where(x => x.ReceivedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(x => x.ReceivedAt <= to.Value);

        return await query
            .OrderByDescending(x => x.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<MessageDeadLetter?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.DeadLetters.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    /// <summary>
    /// Requeue a dead letter — publishes raw JSON back to the bus.
    /// Only Pending status can be retried.
    /// </summary>
    public async Task<bool> RetryAsync(Guid id, CancellationToken ct)
    {
        var letter = await db.DeadLetters.FindAsync([id], ct);
        if (letter is null || letter.Status != DeadLetterStatus.Pending)
            return false;

        // Publish as dynamic object — MassTransit routes by message type header
        await publishEndpoint.Publish<object>(
            new { __payload = letter.Payload, __type = letter.MessageType },
            ctx => ctx.Headers.Set("X-DLQ-Requeued", "true"),
            ct);

        letter.Status = DeadLetterStatus.Retried;
        letter.RetriedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Dead letter {Id} requeued by admin. Type={Type}", id, letter.MessageType);
        return true;
    }

    /// <summary>Quarantine a dead letter — marks it so it won't be retried accidentally.</summary>
    public async Task<bool> QuarantineAsync(Guid id, string reason, CancellationToken ct)
    {
        var letter = await db.DeadLetters.FindAsync([id], ct);
        if (letter is null || letter.Status != DeadLetterStatus.Pending)
            return false;

        letter.Status = DeadLetterStatus.Quarantined;
        letter.QuarantineReason = reason.Length > 1000 ? reason[..1000] : reason;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Dead letter {Id} quarantined. Reason={Reason}", id, reason);
        return true;
    }
}
