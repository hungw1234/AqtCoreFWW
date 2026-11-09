using System.Net.Http.Json;
using AqtCoreFW.Infrastructure.Webhooks;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Alerting;

/// <summary>
/// Hangfire recurring job — evaluates all enabled AlertRules every minute.
/// For each rule:
///   1. Queries Prometheus HTTP API (instant query) for the metric value.
///   2. If condition is breached → RecordBreach() + notify via configured channel.
///   3. If condition clears → ClearBreach().
/// Saves state changes to AlertingDbContext after each evaluation cycle.
/// Registered as a recurring job in AlertingRegistration.RegisterRecurringJobs().
/// </summary>
public sealed class AlertEvaluationJob(
    AlertingDbContext db,
    IHttpClientFactory httpClientFactory,
    WebhookUrlValidator urlValidator,
    IAuditLogger auditLogger,
    IAlertEmailSender alertEmailSender,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<AlertEvaluationJob> logger)
{
    private const string PrometheusClientName = "prometheus";

    // Tag helpers keep the metric tag KEYS spelled in exactly one place — a typo'd key would create a
    // parallel series that no alert rule queries.
    private static KeyValuePair<string, object?> ChannelTag(string channel) => new("channel", channel);

    private static KeyValuePair<string, object?> ReasonTag(string reason) => new("reason", reason);

    // Separates admin-triggered test presses from the scheduled evaluator's own health. The counters
    // mean "the evaluator is going quiet"; a human pressing Test and getting a correct answer back is
    // not that condition, and without this tag three presses would trip the evaluation self-monitor
    // (GT 5 over 5m) and ONE press of a broken EMAIL rule would trip the notification one (GT 0).
    // Alert expressions select the scheduled series explicitly.
    private static KeyValuePair<string, object?> SourceTag(string source) => new("source", source);

    private const string ScheduledSource = "scheduled";
    private const string TestSource = "test";

    // H-01: Recurring job — no retry needed (next scheduled run handles it).
    // DisableConcurrentExecution prevents overlapping runs if evaluation > 1 minute.
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var rules = await db.AlertRules
            .Where(r => r.Enabled)
            .ToListAsync(cancellationToken);

        if (rules.Count == 0)
        {
            logger.LogDebug("AlertEvaluationJob: no enabled rules — skipping");
            return;
        }

        logger.LogInformation("AlertEvaluationJob: evaluating {Count} rule(s)", rules.Count);

        var httpClient = httpClientFactory.CreateClient(PrometheusClientName);

        foreach (var rule in rules)
        {
            try
            {
                await EvaluateRuleAsync(rule, httpClient, cancellationToken);
            }
            catch (Exception ex)
            {
                // Do not let one rule failure abort the entire job cycle
                logger.LogError(ex, "AlertEvaluationJob: error evaluating rule {RuleId} ({Name})",
                    rule.Id, rule.Name);
                AlertingMetrics.EvaluationFailed.Add(
                    1, ReasonTag("rule_error"), SourceTag(ScheduledSource));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // -------------------------------------------------------------------------

    private async Task EvaluateRuleAsync(
        AlertRule rule,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        var metricValue = await QueryPrometheusAsync(
            rule.MetricName, httpClient, ScheduledSource, cancellationToken);

        if (metricValue is null)
        {
            logger.LogWarning(
                "AlertEvaluationJob: no data for metric '{Metric}' (rule '{Name}')",
                rule.MetricName, rule.Name);
            // Also reached when the query threw — QueryPrometheusAsync returns null for both, so a
            // thrown query is counted here as well as under query_failed. See AlertingMetrics.
            AlertingMetrics.EvaluationFailed.Add(
                1, ReasonTag("no_data"), SourceTag(ScheduledSource));
            return;
        }

        if (rule.IsBreached(metricValue.Value))
        {
            rule.RecordBreach();

            logger.LogWarning(
                "Alert breached: {Name} | metric={Metric} value={Value} condition={Cond} threshold={Threshold} breaches={N}",
                rule.Name, rule.MetricName, metricValue.Value,
                rule.Condition, rule.Threshold, rule.ConsecutiveBreaches);

            await auditLogger.LogCommandAsync(
                action: "AlertBreached",
                moduleName: "Alerting",
                resourceType: "AlertRule",
                resourceId: rule.Id.ToString(),
                cancellationToken: cancellationToken);

            // Scheduled path ignores the outcome: EMAIL still rethrows into ExecuteAsync's per-rule
            // catch, WEBHOOK stays swallowed. Only the admin test path reads the return value.
            await DispatchAsync(rule, metricValue.Value, ScheduledSource, cancellationToken);
        }
        else
        {
            if (rule.ConsecutiveBreaches > 0)
            {
                rule.ClearBreach();
                logger.LogInformation(
                    "Alert cleared: {Name} | metric={Metric} value={Value}",
                    rule.Name, rule.MetricName, metricValue.Value);
            }
        }
    }

    /// <summary>
    /// Queries Prometheus instant query API: GET /api/v1/query?query={metric}
    /// Returns the first scalar/vector value, or null if no data.
    /// </summary>
    private async Task<double?> QueryPrometheusAsync(
        string metricName,
        HttpClient httpClient,
        string source,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"api/v1/query?query={Uri.EscapeDataString(metricName)}";
            var response = await httpClient.GetFromJsonAsync<PrometheusQueryResponse>(
                url, cancellationToken);

            if (response?.Status != "success") return null;

            // result[0].value[1] is the string-formatted metric value for instant queries
            var firstResult = response.Data?.Result?.FirstOrDefault();
            if (firstResult?.Value is { Count: >= 2 })
            {
                var rawValue = firstResult.Value[1]?.ToString();
                if (double.TryParse(rawValue,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed))
                    return parsed;
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to query Prometheus for metric '{Metric}'", metricName);
            // A Prometheus outage makes this fire for every rule at once, which is what turns the
            // whole alerting system silent — the case the self-monitor rule watches for.
            AlertingMetrics.EvaluationFailed.Add(1, ReasonTag("query_failed"), SourceTag(source));
            return null;
        }
    }

    /// <summary>
    /// Delivers the breach notification and reports what happened. The scheduled path ignores the
    /// return value and relies on the same exception behaviour as before — EMAIL rethrows so the
    /// per-rule catch in <see cref="ExecuteAsync"/> counts and logs it, WEBHOOK stays swallowed —
    /// while the admin test path reads the outcome to tell the caller whether delivery worked.
    /// </summary>
    private async Task<AlertDispatchOutcome> DispatchAsync(
        AlertRule rule, double metricValue, string source, CancellationToken cancellationToken)
    {
        var message = $"[ALERT] {rule.Name}: {rule.MetricName} = {metricValue} " +
                      $"({rule.Condition} {rule.Threshold}) — breach #{rule.ConsecutiveBreaches}";

        var channel = rule.NotifyChannel.ToUpperInvariant();

        switch (channel)
        {
            case "LOG":
                // Already logged above — explicit Log channel just ensures it is always written
                logger.LogCritical("AlertNotification: {Message}", message);
                return new AlertDispatchOutcome(channel, Delivered: true, Detail: null);

            case "WEBHOOK":
                if (string.IsNullOrEmpty(rule.NotifyTarget))
                    return new AlertDispatchOutcome(channel, false, "No NotifyTarget configured.");
                return await PostWebhookAsync(rule.NotifyTarget, message, channel, source);

            case "EMAIL":
                if (string.IsNullOrEmpty(rule.NotifyTarget))
                {
                    logger.LogWarning(
                        "AlertNotification EMAIL channel has no NotifyTarget for rule {Name} — skipping send",
                        rule.Name);
                    return new AlertDispatchOutcome(channel, false, "No NotifyTarget configured.");
                }

                return await SendEmailAsync(rule, message, channel, source, cancellationToken);

            default:
                logger.LogWarning(
                    "AlertNotification: unknown channel '{Channel}' for rule {Name}",
                    rule.NotifyChannel, rule.Name);
                return new AlertDispatchOutcome(
                    channel, false, $"Unknown notify channel '{rule.NotifyChannel}'.");
        }
    }

    /// <summary>
    /// Evaluates ONE rule and reports the outcome, for the admin test endpoint.
    ///
    /// <para>
    /// Differs from the scheduled path in exactly two ways, both deliberate. The rule is evaluated
    /// whether or not it is <see cref="AlertRule.Enabled"/> — a freshly seeded, still-disabled rule is
    /// the most likely thing an admin wants to test. And NO state is written: no
    /// <see cref="AlertRule.RecordBreach"/>, no <c>ConsecutiveBreaches</c>/<c>LastTriggeredAt</c>
    /// change, no <c>AlertBreached</c> audit entry, no <c>SaveChanges</c>. A test must not forge
    /// operational history.
    /// </para>
    /// <para>
    /// The notification IS really delivered when the rule breaches. A dry run could not have surfaced
    /// the unconfigured EMAIL channel that this endpoint exists to expose. A delivery failure is
    /// returned as <c>Delivered: false</c> rather than thrown, so the caller can report it.
    /// </para>
    /// <para>
    /// Evaluating through the same helpers as the scheduled run means the self-failure counters in
    /// <see cref="AlertingMetrics"/> also move on a test run. Accepted: a test that cannot query
    /// Prometheus is evidence of the same outage the counters exist to record.
    /// </para>
    /// </summary>
    public async Task<AlertRuleTestOutcome> TestRuleAsync(AlertRule rule, CancellationToken cancellationToken = default)
    {
        var httpClient = httpClientFactory.CreateClient(PrometheusClientName);
        var metricValue = await QueryPrometheusAsync(
            rule.MetricName, httpClient, TestSource, cancellationToken);

        if (metricValue is null)
        {
            logger.LogWarning(
                "AlertRule test: no data for metric '{Metric}' (rule '{Name}')",
                rule.MetricName, rule.Name);
            AlertingMetrics.EvaluationFailed.Add(1, ReasonTag("no_data"), SourceTag(TestSource));
            return new AlertRuleTestOutcome(rule.Id, Evaluated: false, MetricValue: null,
                Breached: false, Dispatch: null);
        }

        if (!rule.IsBreached(metricValue.Value))
            return new AlertRuleTestOutcome(rule.Id, true, metricValue.Value,
                Breached: false, Dispatch: null);

        AlertDispatchOutcome dispatch;
        try
        {
            dispatch = await DispatchAsync(rule, metricValue.Value, TestSource, cancellationToken);
        }
        catch (Exception ex)
        {
            // EMAIL rethrows by design so the scheduled path's per-rule catch records it. On the test
            // path the failure IS the answer, so report it instead of faulting the request.
            dispatch = new AlertDispatchOutcome(
                rule.NotifyChannel.ToUpperInvariant(), false, $"delivery_failed: {ex.Message}");
        }

        return new AlertRuleTestOutcome(rule.Id, true, metricValue.Value, Breached: true, dispatch);
    }

    /// <summary>
    /// Sends the alert to a literal address. Two paths log Critical, for different reasons:
    /// an unconfigured relay outside Development returns without sending (a misconfiguration is not
    /// transient — retrying it every minute would only duplicate the log), while a delivery failure
    /// rethrows. Unlike the webhook path neither is swallowed quietly: a breached alert that cannot
    /// reach its recipient is the failure this channel exists to prevent. The per-rule catch in
    /// ExecuteAsync contains the throw, so other rules still evaluate.
    /// </summary>
    private async Task<AlertDispatchOutcome> SendEmailAsync(
        AlertRule rule, string message, string channel, string source, CancellationToken cancellationToken)
    {
        // Email:Host is Vault-supplied and deliberately absent from ProductionSecretValidator.RequiredKeys,
        // so a deployment that never emails still boots. That leaves this path as the only place a missing
        // relay can surface: outside Development, an unset Host would otherwise fall back to the localhost
        // dev default and fail as a connect timeout that reads like a transient network fault.
        if (!environment.IsDevelopment() && string.IsNullOrWhiteSpace(configuration["Email:Host"]))
        {
            logger.LogCritical(
                "Alert email NOT delivered for rule {Name} → {Target}: Email:Host is not configured in {Env}. "
                + "Seed Email__Host (and Email__Password if the relay authenticates) in Vault.",
                rule.Name, rule.NotifyTarget, environment.EnvironmentName);
            AlertingMetrics.NotificationFailed.Add(
                1, ChannelTag("email"), ReasonTag("unconfigured"), SourceTag(source));
            return new AlertDispatchOutcome(channel, false,
                $"Email:Host is not configured in {environment.EnvironmentName}.");
        }

        var subject = $"[ALERT] {rule.Name}";
        try
        {
            await alertEmailSender.SendAsync(rule.NotifyTarget!, subject, message, cancellationToken);
            return new AlertDispatchOutcome(channel, Delivered: true, Detail: null);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "Alert email delivery failed for rule {Name} → {Target} — alert not delivered",
                rule.Name, rule.NotifyTarget);
            AlertingMetrics.NotificationFailed.Add(
                1, ChannelTag("email"), ReasonTag("delivery_failed"), SourceTag(source));
            // Rethrow is load-bearing on the scheduled path: ExecuteAsync's per-rule catch logs it and
            // counts rule_error. TestRuleAsync catches it separately to report the failure instead.
            throw;
        }
    }

    private async Task<AlertDispatchOutcome> PostWebhookAsync(
        string url, string message, string channel, string source)
    {
        // SSRF validation before outgoing request (fix H2-BE-Sec)
        var check = await urlValidator.ValidateAsync(url);
        if (check.IsFailed)
        {
            var reasons = string.Join("; ", check.Errors.Select(e => e.Message));
            logger.LogWarning("Alert webhook SSRF blocked: {Url} — {Reasons}", url, reasons);
            return new AlertDispatchOutcome(channel, false, $"SSRF validation blocked the target: {reasons}");
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            var payload = new { text = message, timestamp = DateTime.UtcNow };
            using var response = await client.PostAsJsonAsync(url, payload);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Alert webhook delivery failed: {Status} {Url}", response.StatusCode, url);
                AlertingMetrics.NotificationFailed.Add(
                    1, ChannelTag("webhook"), ReasonTag("http_error"), SourceTag(source));
                return new AlertDispatchOutcome(channel, false, $"Webhook returned {(int)response.StatusCode}.");
            }

            return new AlertDispatchOutcome(channel, Delivered: true, Detail: null);
        }
        catch (Exception ex)
        {
            // Swallowed, as before: a webhook outage must not abort the rule's evaluation cycle.
            logger.LogWarning(ex, "Alert webhook post threw for url {Url}", url);
            AlertingMetrics.NotificationFailed.Add(
                1, ChannelTag("webhook"), ReasonTag("delivery_failed"), SourceTag(source));
            return new AlertDispatchOutcome(channel, false, $"delivery_failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Result of an admin-triggered test of a single alert rule.
/// <paramref name="Evaluated"/> is false when the metric could not be queried, in which case
/// <paramref name="MetricValue"/> is null. <paramref name="Dispatch"/> is null when the rule did not
/// breach — nothing was sent.
/// </summary>
public sealed record AlertRuleTestOutcome(
    long RuleId,
    bool Evaluated,
    double? MetricValue,
    bool Breached,
    AlertDispatchOutcome? Dispatch);

/// <summary>Outcome of one notification dispatch. <paramref name="Detail"/> is null on success.</summary>
public sealed record AlertDispatchOutcome(
    string Channel,
    bool Delivered,
    string? Detail);
