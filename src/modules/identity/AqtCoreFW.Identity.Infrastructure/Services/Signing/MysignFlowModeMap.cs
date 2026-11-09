using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Single mapping point from the admin-facing <see cref="SignatureFlowMode"/> (Domain) to the
/// provider-level <see cref="MysignSignMode"/> (Infrastructure) that drives the literal Mysign
/// <c>async</c> field.
///
/// <para>Both the generic <c>SignPayloadCommandHandler</c> and the PAdES initiator resolve the
/// provider mode through this helper so the two callers cannot drift to different async values for
/// the same tenant flow setting.</para>
///
/// <para>Mapping: <see cref="SignatureFlowMode.Async"/> → <see cref="MysignSignMode.AsyncPollDeferred"/>
/// (async=2, return fast + confirm later); <see cref="SignatureFlowMode.Sync"/> →
/// <see cref="MysignSignMode.Sync"/> (async=0); <see cref="SignatureFlowMode.AsyncCallback"/> →
/// <see cref="MysignSignMode.AsyncCallback"/> (async=3, webhook).</para>
/// </summary>
public static class MysignFlowModeMap
{
    /// <summary>
    /// Maps a tenant/request <see cref="SignatureFlowMode"/> to the provider <see cref="MysignSignMode"/>.
    /// Throws <see cref="ArgumentOutOfRangeException"/> on an unmapped value (fail-loud — a new flow mode
    /// MUST get an explicit provider mapping rather than silently defaulting).
    /// </summary>
    public static MysignSignMode ToMysignSignMode(SignatureFlowMode flowMode) => flowMode switch
    {
        SignatureFlowMode.Async         => MysignSignMode.AsyncPollDeferred,
        SignatureFlowMode.Sync          => MysignSignMode.Sync,
        SignatureFlowMode.AsyncCallback => MysignSignMode.AsyncCallback,
        _ => throw new ArgumentOutOfRangeException(
            nameof(flowMode), flowMode, "Unmapped SignatureFlowMode — add an explicit MysignSignMode mapping."),
    };
}
