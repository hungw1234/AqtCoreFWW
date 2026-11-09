using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of IMfaEmergencyDisableRequestRepository.
/// Queries [identity].MfaEmergencyDisableRequests — soft-delete filter applied globally.
/// </summary>
public sealed class MfaEmergencyDisableRequestRepository : IMfaEmergencyDisableRequestRepository
{
    private readonly IdentityDbContext _db;

    public MfaEmergencyDisableRequestRepository(IdentityDbContext db) => _db = db;

    public async Task<MfaEmergencyDisableRequest?> GetByIdAsync(
        long id, CancellationToken ct = default)
        => await _db.MfaEmergencyDisableRequests
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<MfaEmergencyDisableRequest?> GetPendingForUserAsync(
        Guid targetUserId, CancellationToken ct = default)
        => await _db.MfaEmergencyDisableRequests
            .Where(r => r.TargetUserId == targetUserId
                     && r.Status == EmergencyDisableStatus.Pending
                     && r.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(
        MfaEmergencyDisableRequest request, CancellationToken ct = default)
    {
        await _db.MfaEmergencyDisableRequests.AddAsync(request, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(
        MfaEmergencyDisableRequest request, CancellationToken ct = default)
    {
        _db.MfaEmergencyDisableRequests.Update(request);
        await _db.SaveChangesAsync(ct);
    }
}
