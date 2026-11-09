using System.Diagnostics.Metrics;

namespace AqtCoreFW.Workflow.Application.Telemetry;

/// <summary>
/// Workflow module perf histograms. Static Meter mirrors the shared
/// <c>OpenTelemetryConfiguration.AqtSource</c> ActivitySource pattern — zero DI churn.
/// Register the Meter name in the OTel pipeline via <c>AddMeter(MeterName)</c>.
/// BP MVP P0 (2026-05-14) — feeds Workflow perf baseline before BP runtime lands.
/// </summary>
public static class WorkflowMetrics
{
    public const string MeterName = "AqtCoreFW.Workflow";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    /// Transition execution latency. Tags: <c>tenant</c>, <c>action_name</c>, <c>result</c> (ok|fail).
    /// </summary>
    public static readonly Histogram<double> ExecuteTransitionDurationMs =
        Meter.CreateHistogram<double>(
            "workflow.execute_transition.duration_ms",
            unit: "ms",
            description: "Workflow transition execution latency.");

    /// <summary>
    /// Available-transitions query latency. Tags: <c>tenant</c>.
    /// </summary>
    public static readonly Histogram<double> AllowedTransitionsQueryDurationMs =
        Meter.CreateHistogram<double>(
            "workflow.allowed_transitions.query_duration_ms",
            unit: "ms",
            description: "GetAvailableTransitions query latency.");
}
