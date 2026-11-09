using AqtCoreFW.Rules.Domain.Entities;

namespace AqtCoreFW.Rules.Domain.Repositories;

/// <summary>
/// Write-side repository for RuleTestCase entities.
/// Query handlers bypass this and use IReadDbConnection (Dapper) directly.
/// T1 entity: long Id only (no PublicId AK).
/// </summary>
public interface IRuleTestCaseRepository
{
    Task<RuleTestCase?> GetByIdAsync(long id, CancellationToken ct = default);
    Task UpdateAsync(RuleTestCase testCase, CancellationToken ct = default);
}
