using AqtCoreFW.Collaboration.Domain.Entities;

namespace AqtCoreFW.Collaboration.Domain.Repositories;

/// <summary>Write-side repository for per-message read receipts (MessageReadState).</summary>
public interface IMessageReadStateRepository
{
    /// <summary>
    /// Idempotently persists a read-receipt. Returns <c>true</c> if it was newly inserted, <c>false</c>
    /// if a receipt already existed for the same (MessageId, UserId) — including the case where a
    /// concurrent first-read won the unique-index race. The caller broadcasts only on a <c>true</c>
    /// result, so re-reads neither duplicate the row nor spam the realtime "seen by" event.
    /// </summary>
    Task<bool> AddIfNotExistsAsync(MessageReadState readState, CancellationToken cancellationToken = default);
}
