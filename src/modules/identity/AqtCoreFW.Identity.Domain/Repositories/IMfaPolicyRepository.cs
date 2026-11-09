using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Domain.Repositories;

/// <summary>Write-side MFA policy repository (QĐ742 1.6.a).</summary>
public interface IMfaPolicyRepository
{
    /// <summary>External addressing — used by controller/command handlers.</summary>
    Task<MfaPolicy?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default);

    /// <summary>Internal addressing — FK lookups within the module.</summary>
    Task<MfaPolicy?> GetByInternalIdAsync(long internalId, CancellationToken ct = default);

    Task<MfaPolicy?> GetByRoleAsync(string roleName, CancellationToken ct = default);
    Task<IReadOnlyList<MfaPolicy>> ListAllAsync(CancellationToken ct = default);

    /// <summary>Returns policies for the given role names — used by MfaPolicyResolver.</summary>
    Task<IReadOnlyList<MfaPolicy>> GetByRoleNamesAsync(
        IEnumerable<string> roleNames, CancellationToken ct = default);

    Task AddAsync(MfaPolicy policy, CancellationToken ct = default);
    Task UpdateAsync(MfaPolicy policy, CancellationToken ct = default);
}
