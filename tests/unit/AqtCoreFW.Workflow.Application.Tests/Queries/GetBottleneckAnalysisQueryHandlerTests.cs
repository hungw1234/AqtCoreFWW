using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.Analytics;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetBottleneckAnalysisQueryHandler.
/// Verifies: happy path, cache hit, tenant isolation, data ordered by dwell time.
/// </summary>
public sealed class GetBottleneckAnalysisQueryHandlerTests
{
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly GetBottleneckAnalysisQueryHandler _handler;

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid DefId = Guid.NewGuid();
    private static readonly DateTimeOffset From = DateTimeOffset.UtcNow.AddDays(-14);
    private static readonly DateTimeOffset To = DateTimeOffset.UtcNow;

    public GetBottleneckAnalysisQueryHandlerTests()
    {
        _handler = new GetBottleneckAnalysisQueryHandler(_db, _cache);
    }

    [Fact]
    public async Task Handle_CacheMiss_QueriesDbAndCaches()
    {
        var rows = new List<BottleneckDto>
        {
            new() { StateId = 1L, StateName = "Approval", AvgDwellMinutes = 480, ActiveCount = 12 },
            new() { StateId = 2L, StateName = "Review",   AvgDwellMinutes = 120, ActiveCount = 5  }
        };
        _cache.GetAsync<List<BottleneckDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<BottleneckDto>?)null);
        _db.QueryAsync<BottleneckDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(rows);

        var result = await _handler.Handle(
            new GetBottleneckAnalysisQuery(DefId, TenantA, From, To), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].AvgDwellMinutes.Should().Be(480);
        await _cache.Received(1).SetAsync(
            Arg.Any<string>(), Arg.Any<List<BottleneckDto>>(),
            Arg.Is<TimeSpan?>(t => t == TimeSpan.FromMinutes(15)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheHit_ReturnsWithoutDbCall()
    {
        var cached = new List<BottleneckDto>
        {
            new() { StateId = 3L, StateName = "Legal", AvgDwellMinutes = 1440, ActiveCount = 3 }
        };
        _cache.GetAsync<List<BottleneckDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(cached);

        var result = await _handler.Handle(
            new GetBottleneckAnalysisQuery(DefId, TenantA, From, To), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].StateName.Should().Be("Legal");
        await _db.DidNotReceive().QueryAsync<BottleneckDto>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TenantIsolation_DifferentTenantsGetDifferentCacheKeys()
    {
        _cache.GetAsync<List<BottleneckDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<BottleneckDto>?)null);
        _db.QueryAsync<BottleneckDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new List<BottleneckDto>());

        await _handler.Handle(new GetBottleneckAnalysisQuery(DefId, TenantA, From, To), CancellationToken.None);
        await _handler.Handle(new GetBottleneckAnalysisQuery(DefId, TenantB, From, To), CancellationToken.None);

        await _cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.Contains(TenantA.ToString())),
            Arg.Any<List<BottleneckDto>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await _cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.Contains(TenantB.ToString())),
            Arg.Any<List<BottleneckDto>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyResult_ReturnsSuccessEmptyList()
    {
        _cache.GetAsync<List<BottleneckDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<BottleneckDto>?)null);
        _db.QueryAsync<BottleneckDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new List<BottleneckDto>());

        var result = await _handler.Handle(
            new GetBottleneckAnalysisQuery(DefId, TenantA, From, To), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
