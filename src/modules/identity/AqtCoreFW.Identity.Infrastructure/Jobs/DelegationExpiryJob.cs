using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Jobs;

/// <summary>
/// Hangfire recurring job — marks Active delegations whose ValidTo has passed as Expired.
/// Also increments the permission version counter so downstream caches self-invalidate.
///
/// Recommended cron: every 15 minutes ("0/15 * * * *") — register in AddIdentityRecurringJobs().
/// </summary>
public sealed class DelegationExpiryJob
{
    private readonly IdentityDbContext _db;
    private readonly IPermissionVersionService _permissionVersion;
    private readonly ILogger<DelegationExpiryJob> _logger;

    public DelegationExpiryJob(
        IdentityDbContext db,
        IPermissionVersionService permissionVersion,
        ILogger<DelegationExpiryJob> logger)
    {
        _db = db;
        _permissionVersion = permissionVersion;
        _logger = logger;
    }

    /// <summary>Hangfire entry point — expire all overdue Active delegations in one batch.</summary>
    public async Task ExecuteAsync()
    {
        var now = DateTime.UtcNow;

        var expired = await _db.UserDelegations
            .Where(d => d.Status == DelegationStatus.Active && d.ValidTo < now)
            .ToListAsync();

        if (expired.Count == 0)
        {
            _logger.LogDebug("DelegationExpiryJob: no delegations to expire.");
            return;
        }

        // Collect unique delegate IDs before mutating — needed for cache invalidation below
        var delegateIds = expired.Select(d => d.DelegateId).Distinct().ToList();

        foreach (var delegation in expired)
            delegation.Expire();

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "DelegationExpiryJob: expired {Count} delegation(s).", expired.Count);

        // Increment permission version for each affected delegate so
        // cached permission sets are treated as stale by consumers.
        foreach (var delegateId in delegateIds)
            await _permissionVersion.IncrementAsync(delegateId);
    }
}
