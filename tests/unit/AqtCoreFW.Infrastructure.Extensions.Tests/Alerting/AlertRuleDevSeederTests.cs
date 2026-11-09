using AqtCoreFW.Infrastructure.Alerting;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Alerting;

/// <summary>
/// <see cref="AlertRuleDevSeeder"/>: config-gated, idempotent, seeds its dev rules DISABLED.
/// Backed by EF InMemory (the seeder's AnyAsync/Add/SaveChanges are provider-agnostic).
/// </summary>
public sealed class AlertRuleDevSeederTests
{
    private const string RuleName = "Signing provider allowlist denials";
    private const string SelfMonitorRuleName = "Alerting evaluation failures";

    // A fresh DI container with an isolated InMemory AlertingDbContext. The database name is fixed ONCE per
    // provider (not regenerated per DbContext instantiation) so every scope shares the same in-memory store.
    private static ServiceProvider BuildProvider()
    {
        var dbName = $"alert-seed-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AlertingDbContext>(o => o.UseInMemoryDatabase(dbName));
        return services.BuildServiceProvider();
    }

    private static IConfiguration Config(string? flag) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(flag is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["Alerting:DevSeed:Enabled"] = flag })
            .Build();

    private static AlertRuleDevSeeder Sut(ServiceProvider provider, string? flag) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(),
            Config(flag),
            NullLogger<AlertRuleDevSeeder>.Instance);

    private static async Task<List<AlertRule>> RulesAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlertingDbContext>();
        return await db.AlertRules.AsNoTracking().ToListAsync();
    }

    [Theory]
    [InlineData(null)]      // key absent
    [InlineData("false")]
    public async Task FlagOffOrAbsent_SeedsNothing(string? flag)
    {
        using var provider = BuildProvider();

        await Sut(provider, flag).StartAsync(CancellationToken.None);

        (await RulesAsync(provider)).Should().BeEmpty();
    }

    [Fact]
    public async Task FlagOn_SeedsTheAllowlistDenyRule_Disabled_WithExpectedSpec()
    {
        using var provider = BuildProvider();

        await Sut(provider, "true").StartAsync(CancellationToken.None);

        var rule = (await RulesAsync(provider)).Single(r => r.Name == RuleName);
        rule.Enabled.Should().BeFalse("the dev seed is INACTIVE — ops enables it per environment");
        rule.MetricName.Should().Be("sum(increase(signing_provider_allowlist_denied_total[5m]))");
        rule.Condition.Should().Be("GT");
        rule.Threshold.Should().Be(5);
        rule.WindowMinutes.Should().Be(5);
        rule.NotifyChannel.Should().Be("Log");
        rule.NotifyTarget.Should().BeNull();
    }

    [Fact]
    public async Task FlagOn_SeedsTheSelfMonitorRule_Disabled_OnTheLogChannel()
    {
        using var provider = BuildProvider();

        await Sut(provider, "true").StartAsync(CancellationToken.None);

        var rule = (await RulesAsync(provider)).Single(r => r.Name == SelfMonitorRuleName);
        rule.Enabled.Should().BeFalse("the dev seed is INACTIVE — ops enables it per environment");
        // Must match the name the OTel Prometheus exporter actually exports for the
        // alerting.evaluation.failed counter; a mismatch makes the rule silent forever.
        // Scoped to source="scheduled" so admin test presses, which record to the same counter,
        // cannot breach a rule that is meant to watch the scheduled evaluator's health.
        rule.MetricName.Should().Be(
            "sum(increase(alerting_evaluation_failed_total{source=\"scheduled\"}[5m]))");
        rule.Condition.Should().Be("GT");
        rule.Threshold.Should().Be(5,
            "one continuously failing rule yields ~10 per 5m window, so the threshold must sit below "
            + "that to fire at a single enabled rule, yet above a lone transient blip");
        rule.WindowMinutes.Should().Be(5);
        rule.NotifyChannel.Should().Be("Log",
            "a rule watching notification failures must not report through a channel it monitors");
        rule.NotifyTarget.Should().BeNull();
    }

    [Fact]
    public async Task FlagOn_IsIdempotent_SecondRunAddsNoDuplicate()
    {
        using var provider = BuildProvider();

        await Sut(provider, "true").StartAsync(CancellationToken.None);
        var afterFirstRun = await RulesAsync(provider);
        await Sut(provider, "true").StartAsync(CancellationToken.None);

        // Pinned against the first run rather than a hard-coded count, so adding a dev rule cannot
        // silently weaken this into asserting nothing.
        (await RulesAsync(provider)).Select(r => r.Name)
            .Should().BeEquivalentTo(afterFirstRun.Select(r => r.Name));
    }

    [Fact]
    public async Task FlagOn_NeverResets_AnOpsEnabledExistingRule()
    {
        // Teeth for "the seed never touches an existing rule — ops owns Enabled/threshold". Seed once, then
        // simulate ops ENABLING it, then seed again: the second run must skip-by-Name and leave Enabled=true.
        // (A bug that re-created or overwrote the rule would flip it back to disabled → this test fails.)
        using var provider = BuildProvider();
        await Sut(provider, "true").StartAsync(CancellationToken.None);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AlertingDbContext>();
            var rule = await db.AlertRules.SingleAsync(r => r.Name == RuleName);
            rule.Enable(Guid.NewGuid());            // ops enables the seeded rule
            await db.SaveChangesAsync();
        }

        await Sut(provider, "true").StartAsync(CancellationToken.None);

        var rules = await RulesAsync(provider);
        rules.Should().ContainSingle(r => r.Name == RuleName, "re-seeding must not duplicate the rule");
        rules.Single(r => r.Name == RuleName).Enabled
            .Should().BeTrue("the seed must never reset an existing ops-tuned rule");
    }
}
