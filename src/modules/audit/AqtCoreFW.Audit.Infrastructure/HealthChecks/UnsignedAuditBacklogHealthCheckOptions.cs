namespace AqtCoreFW.Audit.Infrastructure.HealthChecks;

/// <summary>
/// Thresholds for <see cref="UnsignedAuditBacklogHealthCheck"/>.
/// Both values are config-able so the operator can tune without a redeploy.
/// Conservative defaults chosen so a brief Hangfire outage (minutes) does not page on-call,
/// but a stuck signer that has accumulated hundreds of unprocessed entries does.
///
/// Bind from config section <c>Audit:HealthChecks:UnsignedBacklog</c> in InfrastructureRegistration.
/// </summary>
public sealed class UnsignedAuditBacklogHealthCheckOptions
{
    /// <summary>
    /// Maximum number of unsigned rows (SequenceId=0 or HmacSignature='') tolerated before
    /// the check reports Unhealthy. Rows accumulate when the Hangfire signing job is delayed
    /// or stopped. Default: 100 (roughly one Hangfire-job-skip worth of audit activity).
    /// </summary>
    public int MaxUnsignedCount { get; set; } = 100;

    /// <summary>
    /// Maximum age of the oldest unsigned entry. Entries older than this have been waiting
    /// longer than the expected signing latency — indicates a stuck signer, not a transient delay.
    /// Default: 10 minutes — two missed runs of the <c>audit.chain-sweep</c> recurring job
    /// (<c>AuditChainSweepJob</c>, 5-minute interval), which re-signs any backlog the append-time
    /// enqueue did not pick up. Older than this means the sweep itself is stuck, not a dormant chain.
    /// </summary>
    public TimeSpan MaxOldestUnsignedAge { get; set; } = TimeSpan.FromMinutes(10);
}
