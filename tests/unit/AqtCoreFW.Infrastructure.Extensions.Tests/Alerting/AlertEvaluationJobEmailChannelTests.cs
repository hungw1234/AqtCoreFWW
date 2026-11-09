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
/// <see cref="AlertEvaluationJob"/> notification dispatch: only the EMAIL channel may reach
/// <see cref="IAlertEmailSender"/>, and it must pass NotifyTarget through as the address.
///
/// Prometheus is stubbed to return a value that breaches every rule under test, so evaluation
/// always reaches SendNotificationAsync.
///
/// Shares <see cref="AlertEvaluationJobCollection"/> with the self-failure metric tests: driving this
/// job records to process-wide static counters that those tests observe through a MeterListener, so
/// the two classes must not run concurrently.
/// </summary>
[Collection(AlertEvaluationJobCollection.Name)]
public sealed class AlertEvaluationJobEmailChannelTests
{
    private const string BreachingMetricJson =
        """{"status":"success","data":{"resultType":"vector","result":[{"value":[0,"99"]}]}}""";

    /// <summary>Returns a fixed Prometheus instant-query payload for any request.</summary>
    private sealed class StubPrometheusHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BreachingMetricJson, Encoding.UTF8, "application/json")
            });
    }

    private static AlertingDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AlertingDbContext>()
            .UseInMemoryDatabase($"alert-eval-{Guid.NewGuid()}")
            .Options;
        return new AlertingDbContext(options);
    }

    private static IHttpClientFactory StubHttpFactory()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        // Hand-rolled handler, not a nested Substitute inside Returns() — a nested Substitute.For
        // in a .Returns(...) corrupts NSubstitute's last-call state.
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new StubPrometheusHandler())
        {
            BaseAddress = new Uri("http://prometheus.test/")
        });
        return factory;
    }

    // Threshold 1 with condition GT is breached by the stubbed metric value of 99.
    private static AlertRule BreachingRule(string channel, string? target) =>
        AlertRule.Create(
            name: $"rule-{channel}",
            metricName: "test_metric",
            condition: "GT",
            threshold: 1,
            windowMinutes: 5,
            notifyChannel: channel,
            notifyTarget: target,
            createdBy: Guid.NewGuid());

    private static IConfiguration Config(string? emailHost) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Host"] = emailHost })
            .Build();

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
        string? emailHost = "smtp.internal",
        string environmentName = "Production") =>
        new(db,
            StubHttpFactory(),
            new WebhookUrlValidator(NullLogger<WebhookUrlValidator>.Instance),
            Substitute.For<IAuditLogger>(),
            emailSender,
            Config(emailHost),
            new StubEnvironment(environmentName),
            NullLogger<AlertEvaluationJob>.Instance);

    private static async Task<(IAlertEmailSender EmailSender, AlertingDbContext Db)> RunAsync(
        string channel, string? target)
    {
        var db = NewDb();
        db.AlertRules.Add(BreachingRule(channel, target));
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IAlertEmailSender>();
        await Sut(db, emailSender).ExecuteAsync();
        return (emailSender, db);
    }

    [Fact]
    public async Task EmailChannel_WithTarget_SendsToThatAddress()
    {
        var (emailSender, _) = await RunAsync("EMAIL", "ops@example.gov.vn");

        await emailSender.Received(1).SendAsync(
            "ops@example.gov.vn",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmailChannel_SubjectAndBody_CarryTheAlert()
    {
        var (emailSender, _) = await RunAsync("EMAIL", "ops@example.gov.vn");

        await emailSender.Received(1).SendAsync(
            Arg.Any<string>(),
            Arg.Is<string>(s => s.Contains("rule-EMAIL")),
            Arg.Is<string>(b => b.Contains("test_metric") && b.Contains("99")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmailChannel_LowercaseValue_StillDispatches()
    {
        // NotifyChannel has no validation at write time; the switch upper-cases before matching.
        var (emailSender, _) = await RunAsync("email", "ops@example.gov.vn");

        await emailSender.Received(1).SendAsync(
            "ops@example.gov.vn", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmailChannel_WithoutTarget_DoesNotSend()
    {
        var (emailSender, _) = await RunAsync("EMAIL", null);

        await emailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default);
    }

    [Theory]
    [InlineData("WEBHOOK")]
    [InlineData("LOG")]
    [InlineData("SLACK")] // unknown channel — must not fall through to email
    public async Task NonEmailChannels_DoNotSendEmail(string channel)
    {
        // WEBHOOK target is a link-local address; SSRF validation rejects it before any request leaves.
        var (emailSender, _) = await RunAsync(channel, "https://169.254.169.254/latest");

        await emailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OutsideDevelopment_UnconfiguredEmailHost_DoesNotSend(string? host)
    {
        // Email:Host is not in ProductionSecretValidator.RequiredKeys, so boot cannot catch this.
        // Without the guard the send would connect to the localhost dev default and fail as a timeout.
        var db = NewDb();
        db.AlertRules.Add(BreachingRule("EMAIL", "ops@example.gov.vn"));
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IAlertEmailSender>();
        await Sut(db, emailSender, emailHost: host).ExecuteAsync();

        await emailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task InDevelopment_UnconfiguredEmailHost_StillSends()
    {
        // Development intentionally keeps the localhost:1025 MailHog default — the guard must not fire.
        var db = NewDb();
        db.AlertRules.Add(BreachingRule("EMAIL", "ops@example.gov.vn"));
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IAlertEmailSender>();
        await Sut(db, emailSender, emailHost: null, environmentName: "Development").ExecuteAsync();

        await emailSender.Received(1).SendAsync(
            "ops@example.gov.vn", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Staging_ConfiguredEmailHost_Sends()
    {
        // The guard is default-deny on anything that is not Development, so a non-prod deployed
        // environment must still send once its Host is seeded.
        var db = NewDb();
        db.AlertRules.Add(BreachingRule("EMAIL", "ops@example.gov.vn"));
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IAlertEmailSender>();
        await Sut(db, emailSender, emailHost: "smtp.internal", environmentName: "Staging").ExecuteAsync();

        await emailSender.Received(1).SendAsync(
            "ops@example.gov.vn", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmailSendThrows_DoesNotAbortTheCycle_AndBreachIsStillRecorded()
    {
        var db = NewDb();
        db.AlertRules.Add(BreachingRule("EMAIL", "ops@example.gov.vn"));
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IAlertEmailSender>();
        emailSender
            .SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("smtp down"));

        // The per-rule catch in ExecuteAsync must contain the SMTP failure.
        var act = async () => await Sut(db, emailSender).ExecuteAsync();
        await act.Should().NotThrowAsync();

        // RecordBreach ran before the failed send, and the cycle still reached SaveChangesAsync.
        var rule = await db.AlertRules.AsNoTracking().SingleAsync();
        rule.ConsecutiveBreaches.Should().Be(1);
    }
}
