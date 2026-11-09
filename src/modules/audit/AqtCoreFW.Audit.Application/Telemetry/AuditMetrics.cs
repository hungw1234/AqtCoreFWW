using System.Diagnostics.Metrics;

namespace AqtCoreFW.Audit.Application.Telemetry;

/// <summary>
/// Audit module ingestion-durability metrics. Static Meter mirrors <c>SigningMetrics</c> /
/// <c>WorkflowMetrics</c> — zero DI churn; the OTel pipeline registers it via
/// <c>AddMeter(MeterName)</c> in <c>OpenTelemetryConfiguration</c>, which exports it on the
/// <c>/metrics-otel</c> scrape endpoint (NOT <c>/metrics</c>, which prometheus-net owns and which
/// cannot see these meters). Exported names replace dots with underscores and take a <c>_total</c>
/// suffix; the annotation-only <c>{event}</c> unit contributes no suffix of its own.
///
/// These three counters make the formerly-silent audit-write swallow (cell #7 L1) observable: a
/// synchronous audit persist that fails is no longer lost, it is enqueued to a durable Hangfire
/// retry — and each transition is counted here so a rate-based <c>AlertRule</c> can fire on the one
/// that actually means data loss (<see cref="IngestionLost"/>). The audit DB is not scraped by the
/// Prometheus-query alert evaluator, so the counter is the alert trigger while the recovered audit
/// row is the per-event forensic record.
/// </summary>
public static class AuditMetrics
{
    public const string MeterName = "AqtCoreFW.Audit";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    /// A synchronous audit persist failed and was handed to the durable retry lane. NOT yet loss —
    /// the row is pending-recoverable. A sustained rate signals audit-DB stress. Tag: <c>module</c>
    /// (bounded by module count). Exported as <c>audit_ingestion_fallback_enqueued_total</c>.
    /// </summary>
    public static readonly Counter<long> IngestionFallbackEnqueued =
        Meter.CreateCounter<long>(
            "audit.ingestion.fallback_enqueued",
            unit: "{event}",
            description: "Audit writes that failed synchronously and were enqueued to the durable retry lane.");

    /// <summary>
    /// The durable retry job successfully persisted (or confirmed already-present) a previously
    /// failed audit row. Tag: <c>module</c>. Exported as <c>audit_ingestion_recovered_total</c>.
    /// </summary>
    public static readonly Counter<long> IngestionRecovered =
        Meter.CreateCounter<long>(
            "audit.ingestion.recovered",
            unit: "{event}",
            description: "Audit rows recovered by the durable ingestion retry job.");

    /// <summary>
    /// TRUE data loss: the synchronous persist failed AND the durable retry enqueue also failed —
    /// the audit event could not be made recoverable. This is the alert-worthy counter. Tag:
    /// <c>module</c>. Exported as <c>audit_ingestion_lost_total</c>.
    /// </summary>
    public static readonly Counter<long> IngestionLost =
        Meter.CreateCounter<long>(
            "audit.ingestion.lost",
            unit: "{event}",
            description: "Audit events lost — synchronous persist and durable retry enqueue both failed.");
}
