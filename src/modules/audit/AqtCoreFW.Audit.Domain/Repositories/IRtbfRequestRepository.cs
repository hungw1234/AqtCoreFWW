using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.ValueObjects;

namespace AqtCoreFW.Audit.Domain.Repositories;

/// <summary>Repository for RtbfRequest — Law 91/2025 RTBF tracking.</summary>
public interface IRtbfRequestRepository
{
    Task AddAsync(RtbfRequest request, CancellationToken cancellationToken = default);
    Task<RtbfRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
