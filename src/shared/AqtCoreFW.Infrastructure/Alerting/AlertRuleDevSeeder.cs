using AqtCoreFW.SharedKernel.Application.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Alerting;

/// <summary>
/// Idempotent startup seeder for a fixed set of <b>disabled</b> dev <see cref="AlertRule"/>s — currently the
/// signing provider-allowlist deny-rate rule. Gated by config <c>Alerting:DevSeed:Enabled</c> (default off; no
/// production auto-seed). Each rule is seeded DISABLED so the every-minute <c>AlertEvaluationJob</c> skips it
/// (it loads only <c>.Where(r => r.Enabled)</c>) — zero noise + no spurious ClearBreach when Prometheus is
/// absent locally; ops enables + tunes the threshold per environment via <c>POST /api/v1/admin/alerts</c> (see
/// the signing-provider-allowlist deny-alert ops runbook).
///
/// <para>The rule LIST (<see cref="DevRules"/>) is the extension seam: a new dev alert rule = one entry.
/// Implements <see cref="IIntegrationTestSeeder"/> so integration fixtures keep it registered while stripping
/// other hosted services (required by <c>SeederMarkerContractTests</c>).</para>
/// </summary>
public sealed class AlertRuleDevSeeder(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AlertRuleDevSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    // Seeder-created audit actor — mirrors DemoWorkflowSeeder's SystemActorId convention.
    private static readonly Guid SeedActorId = Guid.Empty;

    /// <summary>The fixed dev-seed rule set. Adding a new INACTIVE dev AlertRule here is the whole seam.</summary>
    private static readonly IReadOnlyList<AlertRuleSeed> DevRules =
    [
        new AlertRuleSeed(
            Name:          "Signing provider allowlist denials",
            MetricName:    "sum(increase(signing_provider_allowlist_denied_total[5m]))",
            Condition:     "GT",
            Threshold:     5,
            WindowMinutes: 5,
            NotifyChannel: "Log",
            NotifyTarget:  null),

        // Watches the alert evaluator itself: rules that fail to evaluate can neither breach nor
        // clear, so a degrading evaluator goes quiet instead of going loud.
        //
        // SCOPE — this catches SUSTAINED PARTIAL failure, not a total Prometheus outage. It is
        // evaluated by the same job, over the same query path, as every other rule: if Prometheus is
        // unreachable then this rule's own query fails too and it can never breach. Detecting that
        // the whole evaluator is down requires an external signal (a Prometheus-side `up{}` or
        // dead-man's-switch alert), which this repo does not have today. The counter still records
        // the outage for anyone scraping /metrics-otel — it is the RULE that cannot self-detect it.
        //
        // THRESHOLD — with the job running every minute and a failing rule recording two increments
        // per run (a thrown query counts as both query_failed and no_data), a single continuously
        // failing rule produces roughly 10 over a 5-minute window. GT 5 therefore fires even when
        // only one rule is enabled, while staying above the 2 that one transient blip produces.
        //
        // The channel is Log, not Email, on purpose: a rule monitoring notification failures must not
        // report through a channel it is monitoring. With the relay down, an Email self-monitor would
        // try to mail the news of a failed mail, fail again, and notify nobody. Log writes inline with
        // no external dependency — a weaker signal that always fires beats a stronger one that fails
        // exactly when it is needed.
        // SOURCE — scoped to source="scheduled". The same counters also record admin test presses
        // (POST /admin/alerts/{id}/test), which are not evaluator degradation: a human asked and got
        // a correct answer. Untagged, three presses against a typo'd metric name would breach this.
        new AlertRuleSeed(
            Name:          "Alerting evaluation failures",
            MetricName:    "sum(increase(alerting_evaluation_failed_total{source=\"scheduled\"}[5m]))",
            Condition:     "GT",
            Threshold:     5,
            WindowMinutes: 5,
            NotifyChannel: "Log",
            NotifyTarget:  null),
    ];

    public async Task StartAsync(CancellationToken ct)
    {
        // Off by default — explicit opt-in per environment, so production never auto-seeds an alert rule.
        if (!configuration.GetValue<bool>("Alerting:DevSeed:Enabled"))
            return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlertingDbContext>();

        var seeded = 0;
        foreach (var seed in DevRules)
        {
            // Idempotent by Name: skip if the rule already exists. Never re-enable or overwrite — the seed only
            // ever CREATES a disabled rule; once present, ops owns its Enabled/threshold state.
            if (await db.AlertRules.AnyAsync(r => r.Name == seed.Name, ct))
                continue;

            var rule = AlertRule.Create(
                name:          seed.Name,
                metricName:    seed.MetricName,
                condition:     seed.Condition,
                threshold:     seed.Threshold,
                windowMinutes: seed.WindowMinutes,
                notifyChannel: seed.NotifyChannel,
                notifyTarget:  seed.NotifyTarget,
                createdBy:     SeedActorId);
            rule.Disable(SeedActorId);   // seed INACTIVE — evaluator skips it until ops enables it.

            db.AlertRules.Add(rule);
            seeded++;
        }

        if (seeded == 0)
            return;

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "AlertRuleDevSeeder: seeded {Count} disabled dev AlertRule(s). Enable + tune per environment via POST /api/v1/admin/alerts.",
            seeded);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private sealed record AlertRuleSeed(
        string Name,
        string MetricName,
        string Condition,
        double Threshold,
        int WindowMinutes,
        string NotifyChannel,
        string? NotifyTarget);
}
