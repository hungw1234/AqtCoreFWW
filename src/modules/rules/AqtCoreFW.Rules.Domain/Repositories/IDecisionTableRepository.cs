using AqtCoreFW.Rules.Domain.Entities;

namespace AqtCoreFW.Rules.Domain.Repositories;

/// <summary>
/// Write-side repository for DecisionTable entities.
/// Query handlers bypass this and use IReadDbConnection (Dapper) directly.
/// Defence-in-depth: inline tenant filter on reads + strict guards on writes.
/// T1 entity: long Id only (no PublicId AK).
/// </summary>
public interface IDecisionTableRepository
{
    Task<DecisionTable?> GetByIdAsync(long id, CancellationToken ct = default);
    Task AddAsync(DecisionTable table, CancellationToken ct = default);
    Task UpdateAsync(DecisionTable table, CancellationToken ct = default);
}
