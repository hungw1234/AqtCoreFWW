using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Audit.Infrastructure.Persistence;

public sealed class SecurityIncidentRepository(AuditDbContext db) : ISecurityIncidentRepository
{
    public async Task AddAsync(SecurityIncident incident, CancellationToken cancellationToken = default)
    {
        db.SecurityIncidents.Add(incident);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<SecurityIncident?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.SecurityIncidents.FirstOrDefaultAsync(e => e.MessageId == id, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
