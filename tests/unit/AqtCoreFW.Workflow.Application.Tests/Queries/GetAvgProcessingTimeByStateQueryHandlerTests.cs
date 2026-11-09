using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.Analytics;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetAvgProcessingTimeByStateQueryHandler.
/// Verifies: happy path, cache hit (no DB call), tenant isolation via distinct cache keys.
/// </summary>
public sealed class GetAvgProcessingTimeByStateQueryHandlerTests
{
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly GetAvgProcessingTimeByStateQueryHandler _handler;

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid DefId = Guid.NewGuid();
    private static readonly DateTimeOffset From = DateTimeOffset.UtcNow.AddDays(-7);
    private static readonly DateTimeOffset To = DateTimeOffset.UtcNow;

    public GetAvgProcessingTimeByStateQueryHandlerTests()
    {
        _handler = new GetAvgProcessingTimeByStateQueryHandler(_db, _cache);
    }

    [Fact]
    public async Task Handle_CacheMiss_QueriesDbAndCaches()
    {
        var rows = new List<StateProcessingTimeDto>
        {
            new() { StateId = 1L, StateName = "Review", AvgMinutes = 120, MedianMinutes = 90, SampleCount = 10 }
        };
        _cache.GetAsync<List<StateProcessingTimeDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<StateProcessingTimeDto>?)null);
        _db.QueryAsync<StateProcessingTimeDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(rows);

        var result = await _handler.Handle(
            new GetAvgProcessingTimeByStateQuery(DefId, TenantA, From, To), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].AvgMinutes.Should().Be(120);
        await _cache.Received(1).SetAsync(
            Arg.Any<string>(), Arg.Any<List<StateProcessingTimeDto>>(),
            Arg.Is<TimeSpan?>(t => t == TimeSpan.FromMinutes(15)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CacheHit_ReturnsWithoutDbCall()
    {
        var cached = new List<StateProcessingTimeDto>
        {
            new() { StateId = 2L, StateName = "Approval", AvgMinutes = 60, MedianMinutes = 45, SampleCount = 5 }
        };
        _cache.GetAsync<List<StateProcessingTimeDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(cached);

        var result = await _handler.Handle(
            new GetAvgProcessingTimeByStateQuery(DefId, TenantA, From, To), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        await _db.DidNotReceive().QueryAsync<StateProcessingTimeDto>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TenantIsolation_DifferentTenantsGetDifferentCacheKeys()
    {
        _cache.GetAsync<List<StateProcessingTimeDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<StateProcessingTimeDto>?)null);
        _db.QueryAsync<StateProcessingTimeDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new List<StateProcessingTimeDto>());

        await _handler.Handle(new GetAvgProcessingTimeByStateQuery(DefId, TenantA, From, To), CancellationToken.None);
        await _handler.Handle(new GetAvgProcessingTimeByStateQuery(DefId, TenantB, From, To), CancellationToken.None);

        await _cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.Contains(TenantA.ToString())),
            Arg.Any<List<StateProcessingTimeDto>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
        await _cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.Contains(TenantB.ToString())),
            Arg.Any<List<StateProcessingTimeDto>>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyResult_ReturnsSuccessEmptyList()
    {
        _cache.GetAsync<List<StateProcessingTimeDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((List<StateProcessingTimeDto>?)null);
        _db.QueryAsync<StateProcessingTimeDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new List<StateProcessingTimeDto>());

        var result = await _handler.Handle(
            new GetAvgProcessingTimeByStateQuery(DefId, TenantA, From, To), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
