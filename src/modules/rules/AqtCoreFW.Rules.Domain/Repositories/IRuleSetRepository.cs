using AqtCoreFW.Rules.Domain.Entities;

namespace AqtCoreFW.Rules.Domain.Repositories;

/// <summary>
/// Write-side repository for the RuleSet aggregate (includes Rules navigation).
/// Query handlers bypass this and use IReadDbConnection (Dapper) directly.
/// T2 entity: external addressing via PublicId (Guid AK), internal FK via Id (long PK).
/// </summary>
public interface IRuleSetRepository
{
    /// <summary>Returns RuleSet (Rules + Versions loaded) by external Guid PublicId. Null if not found.</summary>
    Task<RuleSet?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default);

    /// <summary>Returns RuleSet (Rules + Versions loaded) by internal long Id (FK lookup). Null if not found.</summary>
    Task<RuleSet?> GetByInternalIdAsync(long internalId, CancellationToken ct = default);

    Task AddAsync(RuleSet ruleSet, CancellationToken ct = default);

    Task UpdateAsync(RuleSet ruleSet, CancellationToken ct = default);
}
