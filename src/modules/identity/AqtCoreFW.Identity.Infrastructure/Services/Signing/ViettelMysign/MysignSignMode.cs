namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;

/// <summary>
/// Viettel Mysign sign mode — drives the <c>async</c> field value in the signHash request
/// and the provider's post-call behaviour.
///
/// <list type="table">
///   <listheader><term>Mode</term><description>async field / behaviour</description></listheader>
///   <item><term>Sync</term><description>async=0 — blocks until mobile confirmation; returns signature directly.</description></item>
///   <item><term>AsyncPollInline</term><description>async=2 — returns {transactionId} immediately; provider polls
///     /requests/status within the same request until terminal status, then returns final SignResult.</description></item>
///   <item><term>AsyncPollDeferred</term><description>async=2 — same HTTP call as AsyncPollInline, but provider
///     returns IsPending=true + TransactionId immediately; caller must invoke the confirm endpoint.</description></item>
///   <item><term>AsyncCallback</term><description>async=3 — webhook callback mode (W6).</description></item>
/// </list>
/// </summary>
public enum MysignSignMode
{
    /// <summary>Sync block mode — spec async=0.</summary>
    Sync = 0,

    /// <summary>
    /// Async mode with in-line polling — spec async=2.
    /// Provider polls until terminal status and returns the final result within the same HTTP request.
    /// </summary>
    AsyncPollInline = 1,

    /// <summary>
    /// Async mode with deferred polling — spec async=2.
    /// Provider returns IsPending=true + TransactionId immediately.
    /// Caller must use /digital-signature/confirm to retrieve the final result.
    /// </summary>
    AsyncPollDeferred = 2,

    /// <summary>Async callback (webhook) mode — spec async=3. Implemented in W6.</summary>
    AsyncCallback = 3,
}
