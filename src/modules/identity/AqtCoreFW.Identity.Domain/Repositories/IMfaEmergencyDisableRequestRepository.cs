using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Domain.Repositories;

/// <summary>Persistence for MFA emergency disable requests (2-admin approval flow).</summary>
public interface IMfaEmergencyDisableRequestRepository
{
    Task<MfaEmergencyDisableRequest?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>Returns the latest Pending request for a target user, if any.</summary>
    Task<MfaEmergencyDisableRequest?> GetPendingForUserAsync(
        Guid targetUserId, CancellationToken ct = default);

    Task AddAsync(MfaEmergencyDisableRequest request, CancellationToken ct = default);
    Task UpdateAsync(MfaEmergencyDisableRequest request, CancellationToken ct = default);
}
