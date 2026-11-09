namespace AqtCoreFW.SharedKernel.Application;

/// <summary>
/// Canonical Hangfire queue names. A name used at an <see cref="IBackgroundJobService"/> enqueue call site
/// MUST also be in the processing server's queue list (HangfireRegistration), otherwise the job is enqueued
/// to a queue no worker listens on and sits there forever. Sharing one constant across both sides removes
/// that silent typo-drift failure mode — change the queue name in one place and both ends stay in sync.
/// </summary>
public static class BackgroundJobQueues
{
    /// <summary>Dedicated lane for ClamAV virus scanning (phase 2 of the two-phase file upload).</summary>
    public const string VirusScan = "virus-scan";

    /// <summary>Dedicated lane for audit HMAC-chain computation — tamper-evidence integrity, kept off the
    /// generic background/default lanes so chaining isn't starved by bulk background work.</summary>
    public const string Audit = "audit";
}
