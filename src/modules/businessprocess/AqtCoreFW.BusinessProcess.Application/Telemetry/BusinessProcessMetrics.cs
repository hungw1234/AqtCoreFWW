using System.Diagnostics.Metrics;

namespace AqtCoreFW.BusinessProcess.Application.Telemetry;

/// <summary>
/// BusinessProcess module perf histograms. Static Meter mirrors <c>WorkflowMetrics</c> (BP MVP P0 precedent) —
/// zero DI churn, OTel pipeline registers via <c>AddMeter(MeterName)</c> in <c>OpenTelemetryConfiguration</c>.
/// </summary>
public static class BusinessProcessMetrics
{
    public const string MeterName = "AqtCoreFW.BusinessProcess";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    /// Prefill resolution latency. Tags: <c>tenant</c>, <c>result</c> (ok|not_found).
    /// </summary>
    public static readonly Histogram<double> PrefillDurationMs =
        Meter.CreateHistogram<double>(
            "bp.prefill.duration_ms",
            unit: "ms",
            description: "BP form prefill-context resolution latency.");
}
