using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Domain.Repositories;
using AqtCoreFW.Rules.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Rules.Infrastructure.Repositories;

public sealed class RuleTestCaseRepository(RulesDbContext dbContext) : IRuleTestCaseRepository
{
    public async Task<RuleTestCase?> GetByIdAsync(long id, CancellationToken ct = default)
        => await dbContext.RuleTestCases
            .FirstOrDefaultAsync(tc => tc.Id == id, ct);

    public async Task UpdateAsync(RuleTestCase testCase, CancellationToken ct = default)
    {
        if (dbContext.Entry(testCase).State == EntityState.Detached)
            dbContext.RuleTestCases.Update(testCase);

        await dbContext.SaveChangesAsync(ct);
    }
}
