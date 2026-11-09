using AqtCoreFW.Ai.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.AI;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Monolith in-process implementation of IAiModelProfileProvider.
/// Queries AiDbContext directly — no HTTP hop needed in single-process deployment.
/// </summary>
public sealed class InProcessAiModelProfileProvider(AiDbContext db) : IAiModelProfileProvider
{
    public async Task<AiModelProfileInfo?> GetDefaultProfileAsync(CancellationToken ct = default)
    {
        var profile = await db.AiModelProfiles
            .Where(p => p.IsDefault && p.IsActive)
            .FirstOrDefaultAsync(ct);

        return profile is null ? null : MapToInfo(profile);
    }

    public async Task<AiModelProfileInfo?> GetProfileByProviderAsync(
        string provider,
        CancellationToken ct = default)
    {
        var profile = await db.AiModelProfiles
            .Where(p => p.Provider.ToString() == provider && p.IsActive)
            .OrderByDescending(p => p.IsDefault)
            .FirstOrDefaultAsync(ct);

        return profile is null ? null : MapToInfo(profile);
    }

    public async Task<IReadOnlyList<AiModelProfileInfo>> GetActiveProfilesAsync(
        CancellationToken ct = default)
    {
        var profiles = await db.AiModelProfiles
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);

        return profiles.Select(MapToInfo).ToList();
    }

    private static AiModelProfileInfo MapToInfo(Domain.Entities.AiModelProfile p) => new(
        p.Id, p.Name, p.Provider.ToString(), p.ModelId,
        p.Endpoint, p.MaxTokens, p.Temperature, p.CostPerToken, p.IsDefault);
}
