using System.Diagnostics.Metrics;

namespace AqtCoreFW.Infrastructure.Alerting;

/// <summary>
/// Counters for the alerting job's OWN failures. Static Meter mirrors <c>AuditMetrics</c> /
/// <c>SigningMetrics</c> — zero DI churn; the OTel pipeline registers it via
/// <c>AddMeter(MeterName)</c> in <c>OpenTelemetryConfiguration</c>, which exports it on the
/// <c>/metrics-otel</c> scrape endpoint.
///
/// <para>Every failure inside <see cref="AlertEvaluationJob"/> is logged and then swallowed or
/// returned from, so an alerting system that cannot deliver — or cannot evaluate at all — stays
/// silent while looking healthy. Logs go to console and a rolling file with no aggregator attached,
/// so nothing watches them. These counters are the signal that can actually be alerted on: the job
/// queries Prometheus, so a rule over its own exported counters closes the loop.</para>
///
/// <para><b>Tags are deliberately closed sets.</b> Neither counter is tagged with the rule's name or
/// notify target: a rule name is operator-supplied and unbounded, and an EMAIL rule's target is a
/// person's address — PII that scraping would copy into Prometheus and onto dashboards. Per-rule
/// identity stays in the log line and the audit row, which is where per-event triage happens.</para>
/// </summary>
public static class AlertingMetrics
{
    public const string MeterName = "AqtCoreFW.Alerting";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    /// A breached rule could not be delivered to its channel — the alert fired and nobody was told.
    /// Tags: <c>channel</c> (<c>email</c>, <c>webhook</c>) and <c>reason</c> (<c>unconfigured</c>,
    /// <c>delivery_failed</c>, <c>http_error</c>), both closed sets fixed in source, so the series
    /// count is bounded by a small constant regardless of how many rules or tenants exist.
    /// Exported as <c>alerting_notification_failed_total</c>.
    /// </summary>
    public static readonly Counter<long> NotificationFailed =
        Meter.CreateCounter<long>(
            "alerting.notification.failed",
            unit: "{failure}",
            description: "Breached alert rules whose notification could not be delivered.");

    /// <summary>
    /// A rule could not be evaluated, so it can neither breach nor clear. Tag: <c>reason</c>
    /// (<c>query_failed</c>, <c>no_data</c>, <c>rule_error</c> — a closed set).
    ///
    /// <para>This is the counter worth alerting on. When Prometheus is unreachable every query
    /// returns null, so EVERY rule reads as non-breaching at once and the whole alerting system goes
    /// quiet without a single error surfacing to anyone.</para>
    ///
    /// <para>A thrown query increments <c>query_failed</c> and then <c>no_data</c>, because
    /// <c>QueryPrometheusAsync</c> returns null for both a throw and a genuinely empty result and the
    /// caller cannot distinguish them. The double count is accepted: the two reasons mean different
    /// things operationally, and narrowing them would mean changing that method's return contract.</para>
    ///
    /// Exported as <c>alerting_evaluation_failed_total</c>.
    /// </summary>
    public static readonly Counter<long> EvaluationFailed =
        Meter.CreateCounter<long>(
            "alerting.evaluation.failed",
            unit: "{failure}",
            description: "Alert rule evaluations that could not complete (query failure, missing data, or a rule error).");
}
