namespace AqtCoreFW.SystemParams.Application.Services;

/// <summary>
/// SystemParams-internal management contract — extends the cross-module read contract
/// (<see cref="AqtCoreFW.SharedKernel.Contracts.IFeatureFlagService"/>) with cache
/// invalidation/reload used by FeatureFlagSubscriber + FeatureFlagRefreshService.
/// IsEnabled is inherited so the sync-read signature has a single canonical declaration.
/// </summary>
public interface IFeatureFlagService : AqtCoreFW.SharedKernel.Contracts.IFeatureFlagService
{
    void Invalidate(string key);
    Task ReloadAllAsync(CancellationToken ct = default);
}
