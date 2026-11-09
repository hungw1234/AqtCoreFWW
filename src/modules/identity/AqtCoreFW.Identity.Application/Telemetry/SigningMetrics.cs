using System.Diagnostics.Metrics;

namespace AqtCoreFW.Identity.Application.Telemetry;

/// <summary>
/// Identity module signing metrics. Static Meter mirrors <c>CollaborationMetrics</c>/<c>WorkflowMetrics</c> —
/// zero DI churn; the OTel pipeline registers it via <c>AddMeter(MeterName)</c> in
/// <c>OpenTelemetryConfiguration</c>, which exports it on the <c>/metrics-otel</c> scrape endpoint (NOT
/// <c>/metrics</c>, which prometheus-net owns and which cannot see these meters). Exported names replace
/// dots with underscores and take a <c>_total</c> suffix; the annotation-only <c>{denial}</c> unit
/// contributes no suffix of its own.
/// </summary>
public static class SigningMetrics
{
    public const string MeterName = "AqtCoreFW.Identity";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    /// Signing requests denied by the provider-allowlist guard. Emitted on every deny alongside the forensic
    /// audit row — the audit DB is not scraped by the Prometheus-query alert evaluator, so a rate-based
    /// <c>AlertRule</c> over this counter is the alert trigger while the audit row is the per-event forensic
    /// record. A sustained deny RATE is a mixed-cause signal (a real FE-bypass attempt AND a legitimate
    /// enrolled-wrong-kind retry share the <c>not_enrolled</c> reason) — triage via the audit rows, which carry
    /// <c>signerUserId</c> + <c>requiredKind</c> per event. Tags: <c>provider</c>, <c>tenant</c>, <c>reason</c>
    /// (all bounded — <c>provider</c>/<c>reason</c> by registered keys, <c>tenant</c> by tenant count).
    /// Exported as <c>signing_provider_allowlist_denied_total</c>.
    /// </summary>
    public static readonly Counter<long> ProviderAllowlistDenied =
        Meter.CreateCounter<long>(
            "signing.provider_allowlist.denied",
            unit: "{denial}",
            description: "Signing requests denied by the provider allowlist guard (enrollment-kind mismatch / unmapped provider).");
}
