using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.Analytics;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

/// <summary>
/// Validator tests for the four analytics queries that accept a date range.
/// M3 fix: all four handlers reject date ranges exceeding 365 days → Result.Fail (422).
/// </summary>
public sealed class AnalyticsDateRangeValidatorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DefId   = Guid.NewGuid();

    private static DateTimeOffset Base => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ── GetAvgProcessingTimeByStateQuery ────────────────────────────────────

    [Fact]
    public void ProcessingTime_RangeExceeds365Days_Fails()
    {
        var v = new GetAvgProcessingTimeByStateQueryValidator();
        var result = v.Validate(new GetAvgProcessingTimeByStateQuery(DefId, TenantId, Base, Base.AddDays(400)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "DateRange");
    }

    [Fact]
    public void ProcessingTime_RangeExactly365Days_Passes()
    {
        var v = new GetAvgProcessingTimeByStateQueryValidator();
        var result = v.Validate(new GetAvgProcessingTimeByStateQuery(DefId, TenantId, Base, Base.AddDays(365)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ProcessingTime_FromAfterTo_Fails()
    {
        var v = new GetAvgProcessingTimeByStateQueryValidator();
        var result = v.Validate(new GetAvgProcessingTimeByStateQuery(DefId, TenantId, Base.AddDays(10), Base));
        Assert.False(result.IsValid);
    }

    // ── GetThroughputByUserQuery ─────────────────────────────────────────────

    [Fact]
    public void Throughput_RangeExceeds365Days_Fails()
    {
        var v = new GetThroughputByUserQueryValidator();
        var result = v.Validate(new GetThroughputByUserQuery(TenantId, Base, Base.AddDays(400), GroupByPeriod.Month));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "DateRange");
    }

    [Fact]
    public void Throughput_RangeExactly365Days_Passes()
    {
        var v = new GetThroughputByUserQueryValidator();
        var result = v.Validate(new GetThroughputByUserQuery(TenantId, Base, Base.AddDays(365), GroupByPeriod.Month));
        Assert.True(result.IsValid);
    }

    // ── GetSlaComplianceTrendQuery ───────────────────────────────────────────

    [Fact]
    public void SlaCompliance_RangeExceeds365Days_Fails()
    {
        var v = new GetSlaComplianceTrendQueryValidator();
        var result = v.Validate(new GetSlaComplianceTrendQuery(TenantId, Base, Base.AddDays(400), GroupByPeriod.Month));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "DateRange");
    }

    [Fact]
    public void SlaCompliance_RangeExactly365Days_Passes()
    {
        var v = new GetSlaComplianceTrendQueryValidator();
        var result = v.Validate(new GetSlaComplianceTrendQuery(TenantId, Base, Base.AddDays(365), GroupByPeriod.Month));
        Assert.True(result.IsValid);
    }

    // ── GetBottleneckAnalysisQuery ───────────────────────────────────────────

    [Fact]
    public void Bottleneck_RangeExceeds365Days_Fails()
    {
        var v = new GetBottleneckAnalysisQueryValidator();
        var result = v.Validate(new GetBottleneckAnalysisQuery(DefId, TenantId, Base, Base.AddDays(400)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "DateRange");
    }

    [Fact]
    public void Bottleneck_RangeExactly365Days_Passes()
    {
        var v = new GetBottleneckAnalysisQueryValidator();
        var result = v.Validate(new GetBottleneckAnalysisQuery(DefId, TenantId, Base, Base.AddDays(365)));
        Assert.True(result.IsValid);
    }
}
