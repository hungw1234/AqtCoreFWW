using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.ValueObjects;

namespace AqtCoreFW.Audit.Domain.Repositories;

/// <summary>Repository for SecurityIncident — supports admin read/write.</summary>
public interface ISecurityIncidentRepository
{
    Task AddAsync(SecurityIncident incident, CancellationToken cancellationToken = default);
    Task<SecurityIncident?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
