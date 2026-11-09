using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.Analytics;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetSlaComplianceTrendQueryHandler.
/// Verifies: happy path, cache hit, tenant isolation, all GroupByPeriod values.
/// </summary>
public sealed class GetSlaComplianceTrendQueryHandlerTests
{
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly GetSlaComplianceTrendQueryHandler _handler;

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly DateTimeOffset From = DateTimeOffset.UtcNow.AddMonths(-3);
    private static readonly DateTimeOffset To = DateTimeOffset.UtcNow;

    public GetSlaComplianceTrendQueryHandlerTests()
    {
        _handler = new GetSlaComplianceTrendQueryHandler(_db, _cache);
    }

    [Fact]
    public async Task Handle_CacheMiss_QueriesDbAndCaches()
    {
        var rows = new List<SlaComplianceTrendDto>
        {
            new() { Period = "2026-04", Total = 20, OnTime = 18, CompliancePercent = 90.0m }
        };
        _cache.GetAsync<List<SlaComplianceTrendDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<SlaComplianceTrendDto>?)null);
        _db.QueryAsync<SlaComplianceTrendDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(rows);

        var result = await _handler.Handle(
            new GetSlaComplianceTrendQuery(TenantA, From, To, GroupByPeriod.Month), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].CompliancePercent.Should().Be(90.0m);
        await _cache.Received(1).SetAsync(
            Arg.Any<string>(), Arg.Any<List<SlaComplianceTrendDto>>(),
            Arg.Is<TimeSpan?>(t => t == TimeSpan.FromMinutes(15)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheHit_ReturnsWithoutDbCall()
    {
        var cached = new List<SlaComplianceTrendDto>
        {
            new() { Period = "2026-04", Total = 10, OnTime = 10, CompliancePercent = 100m }
        };
        _cache.GetAsync<List<SlaComplianceTrendDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(cached);

        var result = await _handler.Handle(
            new GetSlaComplianceTrendQuery(TenantA, From, To, GroupByPeriod.Month), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _db.DidNotReceive().QueryAsync<SlaComplianceTrendDto>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TenantIsolation_DifferentTenantsGetDifferentCacheKeys()
    {
        _cache.GetAsync<List<SlaComplianceTrendDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<SlaComplianceTrendDto>?)null);
        _db.QueryAsync<SlaComplianceTrendDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new List<SlaComplianceTrendDto>());

        await _handler.Handle(new GetSlaComplianceTrendQuery(TenantA, From, To, GroupByPeriod.Month), CancellationToken.None);
        await _handler.Handle(new GetSlaComplianceTrendQuery(TenantB, From, To, GroupByPeriod.Month), CancellationToken.None);

        await _cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.Contains(TenantA.ToString())),
            Arg.Any<List<SlaComplianceTrendDto>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await _cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.Contains(TenantB.ToString())),
            Arg.Any<List<SlaComplianceTrendDto>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(GroupByPeriod.Day)]
    [InlineData(GroupByPeriod.Week)]
    [InlineData(GroupByPeriod.Month)]
    public async Task Handle_AllGroupByValues_Succeed(GroupByPeriod groupBy)
    {
        _cache.GetAsync<List<SlaComplianceTrendDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<SlaComplianceTrendDto>?)null);
        _db.QueryAsync<SlaComplianceTrendDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new List<SlaComplianceTrendDto>());

        var result = await _handler.Handle(
            new GetSlaComplianceTrendQuery(TenantA, From, To, groupBy), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// M1 safety: period with zero completed instances returns no GROUP BY rows.
    /// DB returns empty list — handler must return success with empty list, not throw.
    /// (SQL divide-by-zero is impossible: GROUP BY guarantees ≥1 row per group;
    ///  zero-instance periods simply produce no rows at all.)
    /// </summary>
    [Fact]
    public async Task Handle_NoPeriodRows_ZeroCompletedInstances_ReturnsSuccessEmptyList()
    {
        _cache.GetAsync<List<SlaComplianceTrendDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<SlaComplianceTrendDto>?)null);
        _db.QueryAsync<SlaComplianceTrendDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new List<SlaComplianceTrendDto>());   // empty — no completed instances in period

        var result = await _handler.Handle(
            new GetSlaComplianceTrendQuery(TenantA, From, To, GroupByPeriod.Month), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
