namespace AqtCoreFW.SharedKernel.Contracts;

/// <summary>
/// Cross-module feature flag lookup — O(1) sync reads from ConcurrentDict cache.
/// Implemented by SystemParams module, consumed by any module.
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>Sync check — reads L0 cache only. Safe for hot paths.</summary>
    bool IsEnabled(string key, string? tenantId = null);
}
