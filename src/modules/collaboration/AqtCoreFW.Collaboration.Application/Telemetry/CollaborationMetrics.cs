using System.Diagnostics.Metrics;

namespace AqtCoreFW.Collaboration.Application.Telemetry;

/// <summary>
/// Collaboration module metrics. Static Meter mirrors <c>WorkflowMetrics</c>/<c>BusinessProcessMetrics</c> —
/// zero DI churn; the OTel pipeline registers it via <c>AddMeter(MeterName)</c> in
/// <c>OpenTelemetryConfiguration</c>, which exports it on the <c>/metrics-otel</c> scrape endpoint (NOT
/// <c>/metrics</c>, which prometheus-net owns and which cannot see these meters).
/// </summary>
public static class CollaborationMetrics
{
    public const string MeterName = "AqtCoreFW.Collaboration";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    /// Realtime SignalR broadcast failures, log-and-swallowed in <c>MessageSentSignalRHandler</c>.
    /// A sustained spike separates a systemic fault (schema drift / identity module down) from
    /// transient misses that clients reconcile on reconnect. Tag: <c>tenant</c>.
    /// Exported as <c>collab_signalr_broadcast_failures_total</c>.
    /// </summary>
    public static readonly Counter<long> BroadcastFailures =
        Meter.CreateCounter<long>(
            "collab.signalr_broadcast.failures",
            unit: "{failure}",
            description: "Realtime SignalR message broadcast failures (best-effort delivery).");
}
