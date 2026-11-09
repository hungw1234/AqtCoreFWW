using System.Diagnostics.Metrics;
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
/// Serialises every test class that drives <see cref="AlertEvaluationJob"/>. The job's counters are
/// static and process-wide, so a <see cref="MeterListener"/> in one class also observes measurements
/// recorded by a class running in parallel — which makes any per-test tag assertion read another
/// test's increments and fail intermittently.
/// </summary>
[CollectionDefinition(AlertEvaluationJobCollection.Name, DisableParallelization = true)]
public sealed class AlertEvaluationJobCollection
{
    public const string Name = "alert-evaluation-job";
}

/// <summary>
/// Every failure inside <see cref="AlertEvaluationJob"/> is logged and then swallowed or returned
/// from, so a job that cannot deliver — or cannot evaluate at all — looks healthy from the outside.
/// These tests pin the counters that make those failures observable, including the tag values: a
/// copy-pasted increment carrying the wrong <c>reason</c> would satisfy a count-only assertion while
/// feeding the wrong series to the alert rule.
/// </summary>
[Collection(AlertEvaluationJobCollection.Name)]
public sealed class AlertEvaluationJobSelfFailureMetricsTests
{
    private const string BreachingMetricJson =
        """{"status":"success","data":{"resultType":"vector","result":[{"value":[0,"99"]}]}}""";

    private const string EmptyMetricJson =
        """{"status":"success","data":{"resultType":"vector","result":[]}}""";

    /// <summary>
    /// Collects measurements for one meter during its own lifetime only. Counters are static and
    /// process-wide, so asserting an absolute cumulative value would be order-dependent under
    /// parallel test execution; scoping to a listener sidesteps that entirely.
    /// </summary>
    private sealed class MeterCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Instrument, long Value, Dictionary<string, string?> Tags)> _measurements = [];
        private readonly Lock _gate = new();

        public MeterCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AlertingMetrics.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                var copied = new Dictionary<string, string?>();
                foreach (var tag in tags)
                    copied[tag.Key] = tag.Value?.ToString();

                lock (_gate)
                    _measurements.Add((instrument.Name, value, copied));
            });
            _listener.Start();
        }

        /// <summary>One entry per measurement recorded for the instrument, holding that measurement's tags.</summary>
        public IReadOnlyList<Dictionary<string, string?>> TagsFor(string instrumentName)
        {
            lock (_gate)
                return _measurements.Where(m => m.Instrument == instrumentName)
                                    .Select(m => m.Tags)
                                    .ToList();
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class StubPrometheusHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
    }

    /// <summary>Fails every request, standing in for an unreachable Prometheus.</summary>
    private sealed class ThrowingPrometheusHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("prometheus unreachable");
    }

    /// <summary>Answers the Prometheus query normally, then fails the webhook POST with a status code.</summary>
    private sealed class WebhookErrorHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(request.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BreachingMetricJson, Encoding.UTF8, "application/json")
                });
    }

    private static AlertingDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AlertingDbContext>()
            .UseInMemoryDatabase($"alert-metrics-{Guid.NewGuid()}")
            .Options;
        return new AlertingDbContext(options);
    }

    private static IHttpClientFactory StubHttpFactory(Func<HttpMessageHandler> handlerFactory)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handlerFactory())
        {
            BaseAddress = new Uri("http://prometheus.test/")
        });
        return factory;
    }

    private static AlertRule BreachingRule(string channel, string? target) =>
        AlertRule.Create(
            name: $"rule-{channel}-{Guid.NewGuid()}",
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
        Func<HttpMessageHandler>? handlerFactory = null,
        string? emailHost = "smtp.internal",
        string environmentName = "Production") =>
        new(db,
            StubHttpFactory(handlerFactory ?? (() => new StubPrometheusHandler(BreachingMetricJson))),
            new WebhookUrlValidator(NullLogger<WebhookUrlValidator>.Instance),
            Substitute.For<IAuditLogger>(),
            emailSender,
            Config(emailHost),
            new StubEnvironment(environmentName),
            NullLogger<AlertEvaluationJob>.Instance);

    [Fact]
    public async Task EmailDeliveryFailure_IsCounted_SoASilentAlertingOutageIsObservable()
    {
        using var capture = new MeterCapture();
        var db = NewDb();
        db.AlertRules.Add(BreachingRule("EMAIL", "ops@example.gov.vn"));
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IAlertEmailSender>();
        emailSender
            .SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("smtp down"));

        await Sut(db, emailSender).ExecuteAsync();

        capture.TagsFor("alerting.notification.failed").Should().ContainSingle()
            .Which.Should().Contain("channel", "email").And.Contain("reason", "delivery_failed");
    }

    [Fact]
    public async Task UnconfiguredEmailHostOutsideDevelopment_IsCounted_NotJustLogged()
    {
        using var capture = new MeterCapture();
        var db = NewDb();
        db.AlertRules.Add(BreachingRule("EMAIL", "ops@example.gov.vn"));
        await db.SaveChangesAsync();

        await Sut(db, Substitute.For<IAlertEmailSender>(), emailHost: null).ExecuteAsync();

        capture.TagsFor("alerting.notification.failed").Should().ContainSingle()
            .Which.Should().Contain("channel", "email").And.Contain("reason", "unconfigured");
    }

    [Fact]
    public async Task SuccessfulEmailDelivery_CountsNothing()
    {
        // Guards the counters against firing on the healthy path, which would make any rate-based
        // alert rule over them permanently breached and therefore useless.
        using var capture = new MeterCapture();
        var db = NewDb();
        db.AlertRules.Add(BreachingRule("EMAIL", "ops@example.gov.vn"));
        await db.SaveChangesAsync();

        await Sut(db, Substitute.For<IAlertEmailSender>()).ExecuteAsync();

        capture.TagsFor("alerting.notification.failed").Should().BeEmpty();
        capture.TagsFor("alerting.evaluation.failed").Should().BeEmpty();
    }

    [Fact]
    public async Task WebhookErrorStatus_IsCounted_TheQuietestFailurePath()
    {
        using var capture = new MeterCapture();
        var db = NewDb();
        // A public IP literal clears WebhookUrlValidator's SSRF check on its fast path; a hostname
        // would need real DNS resolution, which fails offline and returns before the POST is ever made.
        db.AlertRules.Add(BreachingRule("WEBHOOK", "https://93.184.216.34/alerts"));
        await db.SaveChangesAsync();

        await Sut(db, Substitute.For<IAlertEmailSender>(), () => new WebhookErrorHandler()).ExecuteAsync();

        capture.TagsFor("alerting.notification.failed").Should().ContainSingle()
            .Which.Should().Contain("channel", "webhook").And.Contain("reason", "http_error");
    }

    [Fact]
    public async Task UnreachablePrometheus_CountsQueryFailure_TheOutageNoRuleCanDetect()
    {
        // With Prometheus down every rule reads as non-breaching, so the whole alerting system goes
        // quiet without one breach firing. This counter is the only remaining signal.
        using var capture = new MeterCapture();
        var db = NewDb();
        db.AlertRules.Add(BreachingRule("LOG", null));
        await db.SaveChangesAsync();

        await Sut(db, Substitute.For<IAlertEmailSender>(), () => new ThrowingPrometheusHandler()).ExecuteAsync();

        var reasons = capture.TagsFor("alerting.evaluation.failed")
            .Select(t => t["reason"]).ToList();

        // A thrown query returns null, which the caller cannot tell apart from a genuinely empty
        // result — so both reasons are recorded. The double count is intended, not a bug.
        reasons.Should().Contain("query_failed");
        reasons.Should().Contain("no_data");
    }

    [Fact]
    public async Task MetricWithNoData_CountsNoData_WithoutAQueryFailure()
    {
        using var capture = new MeterCapture();
        var db = NewDb();
        db.AlertRules.Add(BreachingRule("LOG", null));
        await db.SaveChangesAsync();

        await Sut(db, Substitute.For<IAlertEmailSender>(),
            () => new StubPrometheusHandler(EmptyMetricJson)).ExecuteAsync();

        capture.TagsFor("alerting.evaluation.failed").Should().ContainSingle()
            .Which.Should().Contain("reason", "no_data");
    }

    // --- source tag: keeps admin test presses out of the evaluator's health signal ---

    [Fact]
    public async Task ScheduledRun_TagsFailuresAsScheduled()
    {
        using var capture = new MeterCapture();
        var db = NewDb();
        db.AlertRules.Add(BreachingRule("LOG", null));
        await db.SaveChangesAsync();

        await Sut(db, Substitute.For<IAlertEmailSender>(),
            () => new StubPrometheusHandler(EmptyMetricJson)).ExecuteAsync();

        capture.TagsFor("alerting.evaluation.failed").Should().ContainSingle()
            .Which.Should().Contain("source", "scheduled");
    }

    [Fact]
    public async Task TestPress_TagsFailuresAsTest_SoTheyAreNotEvaluatorHealth()
    {
        // Both alert expressions select source="scheduled". Were a test press to record as
        // scheduled, three presses against a typo'd metric name would trip the evaluation
        // self-monitor (GT 5 over 5m) and one press of a broken EMAIL rule the notification
        // one (GT 0) — paging ops about a healthy evaluator.
        using var capture = new MeterCapture();
        var db = NewDb();
        var rule = BreachingRule("LOG", null);
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync();

        await Sut(db, Substitute.For<IAlertEmailSender>(),
            () => new StubPrometheusHandler(EmptyMetricJson))
            .TestRuleAsync(rule, CancellationToken.None);

        capture.TagsFor("alerting.evaluation.failed").Should().NotBeEmpty()
            .And.OnlyContain(t => t["source"] == "test");
    }

    [Fact]
    public async Task TestPress_FailedEmailDispatch_TagsNotificationFailureAsTest()
    {
        // The sharper half: AqtCoreFWAlertNotificationFailing fires at GT 0, so a single press
        // against the broken EMAIL rule this endpoint exists to diagnose would page ops.
        using var capture = new MeterCapture();
        var db = NewDb();
        var rule = BreachingRule("EMAIL", "ops@example.gov.vn");
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IAlertEmailSender>();
        emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("relay refused"));

        await Sut(db, emailSender, () => new StubPrometheusHandler(BreachingMetricJson))
            .TestRuleAsync(rule, CancellationToken.None);

        capture.TagsFor("alerting.notification.failed").Should().NotBeEmpty()
            .And.OnlyContain(t => t["source"] == "test");
    }
}
