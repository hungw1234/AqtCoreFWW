using System.Diagnostics.Metrics;

namespace AqtCoreFW.Forms.Application.Telemetry;

/// <summary>
/// Forms module UX-baseline metrics. Static Meter mirrors <c>AuditMetrics</c> /
/// <c>SigningMetrics</c> — zero DI churn; the OTel pipeline registers it via
/// <c>AddMeter(MeterName)</c> in <c>OpenTelemetryConfiguration</c>, which exports it on the
/// <c>/metrics-otel</c> scrape endpoint (NOT <c>/metrics</c>, which prometheus-net owns and which
/// cannot see these meters). Exported names replace dots with underscores; a Counter takes a
/// <c>_total</c> suffix, a Histogram does not; the annotation-only <c>{event}</c> / <c>{errors}</c>
/// units contribute no suffix of their own.
///
/// These instruments give a BE-only baseline for the two launch UX questions the hardening plan
/// tracks: form completion-rate (<see cref="FormSubmitted"/> ÷ <see cref="FormOpened"/>) and form
/// quality (<see cref="ValidationErrorsPerSubmit"/> distribution). FE form-open beaconing is
/// deliberately deferred post-v1.0; completion-rate is derived from the BE open-vs-submit counts,
/// so the "opened" count includes API prefetch/crawler traffic — read the ratio as a trend, not an
/// exact per-user figure.
/// </summary>
public static class FormMetrics
{
    public const string MeterName = "AqtCoreFW.Forms";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    /// A form submission reached the validation stage — counted on BOTH a clean submit and a
    /// validation-rejection so the denominator of "errors per submit" is honest. It sits AFTER the
    /// template-not-found and consent guards and BEFORE signature-verify, so it deliberately does NOT
    /// count submits that die at those earlier gates (template-not-found, consent-missing, signature
    /// rejection). "Completion-rate = submitted ÷ opened" therefore measures validation-stage reach,
    /// not raw click-to-submit intent. Tags: <c>template_code</c>, <c>is_public</c>,
    /// <c>storage_mode</c> — <c>tenant</c> is intentionally NOT a label (one tenant per deployment;
    /// a per-tenant label would be an unbounded-cardinality + tenant-attribution leak, the same class
    /// the trace pipeline strips <c>db.statement</c> to avoid). Exported as
    /// <c>forms_submission_submitted_total</c>.
    /// </summary>
    public static readonly Counter<long> FormSubmitted =
        Meter.CreateCounter<long>(
            "forms.submission.submitted",
            unit: "{event}",
            description: "Form submissions attempted (success + validation rejection).");

    /// <summary>
    /// A PUBLIC form template was loaded by an anonymous caller (the public by-code fetch). v1.0 wires
    /// only the public-open site, so this currently sees public opens only (<c>is_public=true</c>); an
    /// authenticated-open counterpart is deferred. Pairs with <see cref="FormSubmitted"/> to derive
    /// completion-rate. Includes API prefetch/crawler opens (no FE open-beacon in v1.0). Tags:
    /// <c>template_code</c>, <c>is_public</c>. Exported as <c>forms_submission_opened_total</c>.
    /// </summary>
    public static readonly Counter<long> FormOpened =
        Meter.CreateCounter<long>(
            "forms.submission.opened",
            unit: "{event}",
            description: "Form templates loaded by a caller (public by-code fetch or authenticated load).");

    /// <summary>
    /// Distribution of the validation-error count in a single submit attempt (0 for a clean submit).
    /// A rising p50/p90 signals a form design that fights its users — the UX-polish target for the
    /// plan's Phase 3. Tags: <c>is_public</c>, <c>storage_mode</c> only — a Histogram multiplies its
    /// buckets by every label value, so the user-defined <c>template_code</c> (and <c>tenant</c>) are
    /// deliberately NOT labels here to avoid a bucket × unbounded-code cardinality blow-up. Exported
    /// as <c>forms_validation_errors_per_submit</c> (Histogram, no <c>_total</c> suffix).
    /// </summary>
    public static readonly Histogram<int> ValidationErrorsPerSubmit =
        Meter.CreateHistogram<int>(
            "forms.validation.errors_per_submit",
            unit: "{errors}",
            description: "Count of validation errors in a single form-submit attempt.");
}
