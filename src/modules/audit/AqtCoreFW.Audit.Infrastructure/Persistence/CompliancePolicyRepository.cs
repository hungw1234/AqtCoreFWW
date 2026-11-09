using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Audit.Infrastructure.Persistence;

public sealed class CompliancePolicyRepository(AuditDbContext db) : ICompliancePolicyRepository
{
    public async Task AddAsync(CompliancePolicy policy, CancellationToken cancellationToken = default)
    {
        db.CompliancePolicies.Add(policy);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<CompliancePolicy?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        db.CompliancePolicies.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task AddEvaluationAsync(CompliancePolicyEvaluation evaluation, CancellationToken cancellationToken = default)
    {
        db.CompliancePolicyEvaluations.Add(evaluation);
        await db.SaveChangesAsync(cancellationToken);
    }
}
