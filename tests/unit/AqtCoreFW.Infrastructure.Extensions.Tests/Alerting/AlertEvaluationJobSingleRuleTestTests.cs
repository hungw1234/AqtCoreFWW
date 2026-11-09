using System.Net;
using System.Text;
using AqtCoreFW.Infrastructure.Alerting;
using AqtCoreFW.Infrastructure.Webhooks;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Alerting;

/// <summary>
/// <see cref="AlertEvaluationJob.TestRuleAsync"/> — the admin test-button path.
///
/// It must evaluate exactly the rule it was given (the scheduled <c>ExecuteAsync</c> re-runs every
/// enabled rule, which is what this path exists to stop), evaluate it even when disabled, really
/// dispatch the notification, and write no state while doing so.
///
/// Shares <see cref="AlertEvaluationJobCollection"/> with the other job tests: driving the job records
/// to process-wide static counters that the metric tests observe through a MeterListener, so these
/// classes must not run concurrently.
/// </summary>
[Collection(AlertEvaluationJobCollection.Name)]
public sealed class AlertEvaluationJobSingleRuleTestTests
{
    private const string BreachingMetricJson =
        """{"status":"success","data":{"resultType":"vector","result":[{"value":[0,"99"]}]}}""";

    private const string NoDataJson =
        """{"status":"success","data":{"resultType":"vector","result":[]}}""";

    private sealed class StubPrometheusHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
    }

    private static AlertingDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AlertingDbContext>()
            .UseInMemoryDatabase($"alert-test-{Guid.NewGuid()}")
            .Options;
        return new AlertingDbContext(options);
    }

    private static IHttpClientFactory StubHttpFactory(string payload)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        // Hand-rolled handler, not a nested Substitute inside Returns() — a nested Substitute.For
        // in a .Returns(...) corrupts NSubstitute's last-call state.
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new StubPrometheusHandler(payload))
        {
            BaseAddress = new Uri("http://prometheus.test/")
        });
        return factory;
    }

    // Threshold 1 with condition GT is breached by the stubbed metric value of 99.
    private static AlertRule BreachingRule(string channel, string? target, string name = "rule") =>
        AlertRule.Create(
            name: $"{name}-{channel}",
            metricName: "test_metric",
            condition: "GT",
            threshold: 1,
            windowMinutes: 5,
            notifyChannel: channel,
            notifyTarget: target,
            createdBy: Guid.NewGuid());

    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static AlertEvaluationJob Sut(
        AlertingDbContext db,
        IAlertEmailSender emailSender,
        IAuditLogger auditLogger,
        string metricPayload = BreachingMetricJson) =>
        new(db,
            StubHttpFactory(metricPayload),
            new WebhookUrlValidator(NullLogger<WebhookUrlValidator>.Instance),
            auditLogger,
            emailSender,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Host"] = "smtp.internal" })
                .Build(),
            new StubEnvironment("Production"),
            NullLogger<AlertEvaluationJob>.Instance);

    [Fact]
    public async Task TestRuleAsync_DoesNotEvaluateOtherRules()
    {
        // The defect this slice exists to fix: the endpoint called ExecuteAsync, which re-evaluated
        // every enabled rule — firing other rules' real notifications and recording their breaches.
        var db = NewDb();
        var target = BreachingRule("LOG", null, "target");
        var bystander = BreachingRule("EMAIL", "ops@example.gov.vn", "bystander");
        db.AlertRules.AddRange(target, bystander);
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IAlertEmailSender>();
        var outcome = await Sut(db, emailSender, Substitute.For<IAuditLogger>())
            .TestRuleAsync(target, CancellationToken.None);

        // The target really ran — otherwise the assertions below would also hold for a no-op.
        outcome.Breached.Should().BeTrue();

        await emailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default);

        // Reloaded rather than read off the in-memory instance, so the assertion does not depend on
        // the provider handing back the same tracked object.
        var reloaded = await db.AlertRules.AsNoTracking().SingleAsync(r => r.Id == bystander.Id);
        reloaded.ConsecutiveBreaches.Should().Be(0,
            because: "testing one rule must not record a breach against another");
        reloaded.LastTriggeredAt.Should().BeNull();
    }

    [Fact]
    public async Task TestRuleAsync_DisabledRule_IsStillEvaluated()
    {
        // A freshly seeded rule is disabled, which makes it the most likely thing an admin tests.
        // ExecuteAsync filters on Enabled, so the old endpoint silently did nothing for this case.
        var db = NewDb();
        var rule = BreachingRule("LOG", null);
        rule.Disable(Guid.NewGuid());
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync();

        var outcome = await Sut(db, Substitute.For<IAlertEmailSender>(), Substitute.For<IAuditLogger>())
            .TestRuleAsync(rule, CancellationToken.None);

        outcome.Evaluated.Should().BeTrue();
        outcome.Breached.Should().BeTrue();
        outcome.MetricValue.Should().Be(99);
    }

    [Fact]
    public async Task TestRuleAsync_BreachingRule_WritesNoState()
    {
        var db = NewDb();
        var rule = BreachingRule("LOG", null);
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync();

        var auditLogger = Substitute.For<IAuditLogger>();
        await Sut(db, Substitute.For<IAlertEmailSender>(), auditLogger)
            .TestRuleAsync(rule, CancellationToken.None);

        rule.ConsecutiveBreaches.Should().Be(0, because: "a test must not forge operational history");
        rule.LastTriggeredAt.Should().BeNull();
        await auditLogger.DidNotReceiveWithAnyArgs().LogCommandAsync(
            default!, default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task TestRuleAsync_BreachingRule_ReallyDispatches()
    {
        // Guards against "writes no state" being implemented as "does nothing" — verifying the
        // delivery path is the entire point of the button.
        var db = NewDb();
        var rule = BreachingRule("EMAIL", "ops@example.gov.vn");
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IAlertEmailSender>();
        var outcome = await Sut(db, emailSender, Substitute.For<IAuditLogger>())
            .TestRuleAsync(rule, CancellationToken.None);

        await emailSender.Received(1).SendAsync(
            "ops@example.gov.vn", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        outcome.Dispatch!.Delivered.Should().BeTrue();
        outcome.Dispatch.Channel.Should().Be("EMAIL");
    }

    [Fact]
    public async Task TestRuleAsync_EmailSendThrows_ReportsUndelivered()
    {
        // The row's original complaint: a broken EMAIL rule answered 202 while delivery had failed.
        var db = NewDb();
        var rule = BreachingRule("EMAIL", "ops@example.gov.vn");
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IAlertEmailSender>();
        emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("relay refused"));

        var outcome = await Sut(db, emailSender, Substitute.For<IAuditLogger>())
            .TestRuleAsync(rule, CancellationToken.None);

        outcome.Dispatch!.Delivered.Should().BeFalse();
        outcome.Dispatch.Detail.Should().Contain("relay refused",
            because: "the caller needs the reason, not just a failure flag");
    }

    [Fact]
    public async Task TestRuleAsync_MetricUnavailable_ReportsNotEvaluated()
    {
        var db = NewDb();
        var rule = BreachingRule("LOG", null);
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync();

        var outcome = await Sut(db, Substitute.For<IAlertEmailSender>(), Substitute.For<IAuditLogger>(),
                metricPayload: NoDataJson)
            .TestRuleAsync(rule, CancellationToken.None);

        outcome.Evaluated.Should().BeFalse(
            because: "an unqueryable metric is not the same as a rule that did not breach");
        outcome.MetricValue.Should().BeNull();
        outcome.Breached.Should().BeFalse();
        outcome.Dispatch.Should().BeNull();
    }
}
