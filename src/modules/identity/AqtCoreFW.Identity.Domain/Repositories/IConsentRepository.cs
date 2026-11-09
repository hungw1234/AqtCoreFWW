using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Domain.Repositories;

/// <summary>Write-side consent repository — read-side uses IReadDbConnection (Dapper).</summary>
public interface IConsentRepository
{
    /// <summary>Gets consent record by PublicId (Guid AK — T2 entity).</summary>
    Task<ConsentRecord?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default);
    Task<List<ConsentRecord>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<ConsentRecord?> GetByUserAndPurposeAsync(Guid userId, string purpose, CancellationToken ct = default);
    Task AddAsync(ConsentRecord record, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
