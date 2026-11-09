using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.Analytics;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetThroughputByUserQueryHandler.
/// Verifies: happy path, cache hit, tenant isolation, GroupByPeriod invalid value throws.
/// </summary>
public sealed class GetThroughputByUserQueryHandlerTests
{
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly GetThroughputByUserQueryHandler _handler;

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly DateTimeOffset From = DateTimeOffset.UtcNow.AddDays(-30);
    private static readonly DateTimeOffset To = DateTimeOffset.UtcNow;

    public GetThroughputByUserQueryHandlerTests()
    {
        _handler = new GetThroughputByUserQueryHandler(_db, _cache);
    }

    [Fact]
    public async Task Handle_CacheMiss_QueriesDbAndCaches()
    {
        var rows = new List<UserThroughputDto>
        {
            new() { UserId = Guid.NewGuid(), Period = "2026-04-12", TransitionCount = 15 }
        };
        _cache.GetAsync<List<UserThroughputDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<UserThroughputDto>?)null);
        _db.QueryAsync<UserThroughputDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(rows);

        var result = await _handler.Handle(
            new GetThroughputByUserQuery(TenantA, From, To, GroupByPeriod.Day), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].TransitionCount.Should().Be(15);
        await _cache.Received(1).SetAsync(
            Arg.Any<string>(), Arg.Any<List<UserThroughputDto>>(),
            Arg.Is<TimeSpan?>(t => t == TimeSpan.FromMinutes(15)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheHit_ReturnsWithoutDbCall()
    {
        var cached = new List<UserThroughputDto>
        {
            new() { UserId = Guid.NewGuid(), Period = "2026-W15", TransitionCount = 42 }
        };
        _cache.GetAsync<List<UserThroughputDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(cached);

        var result = await _handler.Handle(
            new GetThroughputByUserQuery(TenantA, From, To, GroupByPeriod.Week), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _db.DidNotReceive().QueryAsync<UserThroughputDto>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TenantIsolation_DifferentTenantsGetDifferentCacheKeys()
    {
        _cache.GetAsync<List<UserThroughputDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<UserThroughputDto>?)null);
        _db.QueryAsync<UserThroughputDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserThroughputDto>());

        await _handler.Handle(new GetThroughputByUserQuery(TenantA, From, To, GroupByPeriod.Day), CancellationToken.None);
        await _handler.Handle(new GetThroughputByUserQuery(TenantB, From, To, GroupByPeriod.Day), CancellationToken.None);

        await _cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.Contains(TenantA.ToString())),
            Arg.Any<List<UserThroughputDto>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await _cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.Contains(TenantB.ToString())),
            Arg.Any<List<UserThroughputDto>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(GroupByPeriod.Day)]
    [InlineData(GroupByPeriod.Week)]
    [InlineData(GroupByPeriod.Month)]
    public async Task Handle_AllGroupByValues_Succeed(GroupByPeriod groupBy)
    {
        _cache.GetAsync<List<UserThroughputDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<UserThroughputDto>?)null);
        _db.QueryAsync<UserThroughputDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserThroughputDto>());

        var result = await _handler.Handle(
            new GetThroughputByUserQuery(TenantA, From, To, groupBy), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_InvalidGroupBy_ThrowsArgumentOutOfRange()
    {
        _cache.GetAsync<List<UserThroughputDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<UserThroughputDto>?)null);

        var act = async () => await _handler.Handle(
            new GetThroughputByUserQuery(TenantA, From, To, (GroupByPeriod)99), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // ── ApplyTopNWithOthers unit tests ──────────────────────────────────────────

    [Fact]
    public void ApplyTopNWithOthers_Empty_ReturnsEmpty()
    {
        var result = GetThroughputByUserQueryHandler.ApplyTopNWithOthers([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ApplyTopNWithOthers_FiveOrFewerUsers_NoOthersBucket()
    {
        // Exactly 5 users — all should be kept individually, no "Others" row
        var users = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var rows = users.Select(u => new UserThroughputDto
        {
            UserId = u,
            Period = "2026-04",
            TransitionCount = 10,
        }).ToList();

        var result = GetThroughputByUserQueryHandler.ApplyTopNWithOthers(rows);

        result.Should().HaveCount(5);
        result.Should().NotContain(r => r.IsOthers);
    }

    [Fact]
    public void ApplyTopNWithOthers_EightUsers_ReturnsFiveNamedPlusOneOthers()
    {
        // 8 users across 2 periods → expect 5 named lines + 1 "Others" per period = 5*2 + 2 = 12 rows
        var users = Enumerable.Range(1, 8).Select(i => Guid.NewGuid()).ToList();
        var rows = new List<UserThroughputDto>();
        foreach (var period in new[] { "2026-03", "2026-04" })
        {
            for (int i = 0; i < users.Count; i++)
            {
                // User 0 has most transitions overall (rank 1), user 7 has least (rank 8)
                rows.Add(new UserThroughputDto
                {
                    UserId = users[i],
                    Period = period,
                    TransitionCount = (8 - i) * 10,
                });
            }
        }

        var result = GetThroughputByUserQueryHandler.ApplyTopNWithOthers(rows);

        var topRows = result.Where(r => !r.IsOthers).ToList();
        var othersRows = result.Where(r => r.IsOthers).ToList();

        // 5 top users × 2 periods
        topRows.Should().HaveCount(10);
        // 1 "Others" per period
        othersRows.Should().HaveCount(2);
        othersRows.Should().AllSatisfy(r => r.UserId.Should().Be(Guid.Empty));
    }

    [Fact]
    public void ApplyTopNWithOthers_OthersCount_SumsRemainingUsers()
    {
        // 6 users in 1 period: top 5 sum=150, 6th user=10 → Others=10
        var users = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        var rows = users.Select((u, i) => new UserThroughputDto
        {
            UserId = u,
            Period = "2026-04",
            TransitionCount = (6 - i) * 10, // 60, 50, 40, 30, 20, 10
        }).ToList();

        var result = GetThroughputByUserQueryHandler.ApplyTopNWithOthers(rows);

        var others = result.Single(r => r.IsOthers);
        others.TransitionCount.Should().Be(10); // only the 6th user (rank 6)
        others.Period.Should().Be("2026-04");
    }

    [Fact]
    public void ApplyTopNWithOthers_TopNSelectedByTotalAcrossPeriods()
    {
        // User A: low in period 1 but high in period 2 → total should rank them in top 5
        var userHigh = Guid.NewGuid();
        var userLow = Guid.NewGuid();
        var fillerUsers = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();

        var rows = new List<UserThroughputDto>
        {
            new() { UserId = userHigh, Period = "2026-03", TransitionCount = 1 },
            new() { UserId = userHigh, Period = "2026-04", TransitionCount = 100 }, // total=101 → in top 5
            new() { UserId = userLow, Period = "2026-03", TransitionCount = 5 },
            new() { UserId = userLow, Period = "2026-04", TransitionCount = 5 },    // total=10 → others
        };
        // Add 4 filler users each with total=50 → they occupy ranks 1-4
        foreach (var u in fillerUsers)
        {
            rows.Add(new UserThroughputDto { UserId = u, Period = "2026-04", TransitionCount = 50 });
        }

        var result = GetThroughputByUserQueryHandler.ApplyTopNWithOthers(rows);

        var topUserIds = result.Where(r => !r.IsOthers).Select(r => r.UserId).ToHashSet();
        topUserIds.Should().Contain(userHigh);  // total=101 → in top 5
        topUserIds.Should().NotContain(userLow); // total=10 → aggregated into Others
    }
}
